import Foundation

public enum ApplyStatus: Sendable, Equatable {
    case applied, alreadyAppliedToday, paused, failed, busy, rateLimited
}

public struct ApplyResult: Sendable {
    public var status: ApplyStatus
    public var entries: [HistoryEntry]
    public var error: String?
    public var errorKind: ProviderError.Kind?

    public init(_ status: ApplyStatus, _ entries: [HistoryEntry] = [], error: String? = nil, errorKind: ProviderError.Kind? = nil) {
        self.status = status
        self.entries = entries
        self.error = error
        self.errorKind = errorKind
    }
}

/// Picks a photo, downloads it, puts it on the desktop and records it. Everything is downloaded before the desktop
/// changes, so a failure halfway never leaves a blank or half-changed desktop.
public actor WallpaperService {
    /// Topics tried per screen before giving up.
    static let maxQueriesPerTarget = 3
    /// "Change now" is limited to this many successful changes per hour (every install shares one Unsplash key).
    public static let maxManualChangesPerHour = 2
    static let manualWindow: TimeInterval = 3600

    private let provider: ImageProvider
    private let setter: WallpaperSetter
    private let history: HistoryStore
    private let settings: SettingsStore
    private let topics: TopicPicker
    private let clock: OrielClock
    private let paths: AppPaths
    private let isDarkMode: @Sendable () -> Bool

    public init(provider: ImageProvider, setter: WallpaperSetter, history: HistoryStore, settings: SettingsStore,
                topics: TopicPicker = RandomTopicPicker(), clock: OrielClock = SystemClock(), paths: AppPaths,
                isDarkMode: @escaping @Sendable () -> Bool = { false }) {
        self.provider = provider
        self.setter = setter
        self.history = history
        self.settings = settings
        self.topics = topics
        self.clock = clock
        self.paths = paths
        self.isDarkMode = isDarkMode
    }

    /// When "Change now" works again, or nil if it is available right now.
    public static func manualChangeAvailableAt(_ state: OrielState, now: Date) -> Date? {
        let recent = state.manualChanges.filter { now.timeIntervalSince($0) < manualWindow }.sorted()
        guard recent.count >= maxManualChangesPerHour else { return nil }
        return recent[recent.count - maxManualChangesPerHour].addingTimeInterval(manualWindow)
    }

    /// Whole minutes left until `date`, rounded up, at least 1.
    public static func minutesUntil(_ date: Date, now: Date) -> Int {
        max(1, Int((date.timeIntervalSince(now) / 60).rounded(.up)))
    }

    /// - Parameter force: change even if this tick's wallpaper was already set ("Change now").
    public func apply(force: Bool) async -> ApplyResult {
        let s = settings.load()
        if !force && s.paused { return ApplyResult(.paused) }
        if !force && ScheduleRules.isFresh(s, settings.loadState(), clock.now) { return ApplyResult(.alreadyAppliedToday) }

        // The app and a command-line run can fire at the same moment.
        guard let lock = ApplyLock.tryAcquire(paths.lockFile) else {
            return ApplyResult(.busy, error: "Another change is already in progress.")
        }
        defer { withExtendedLifetime(lock) {} }

        // Re-check under the lock; the other process may have just finished.
        var state = settings.loadState()
        let now = clock.now
        if !force && ScheduleRules.isFresh(s, state, now) { return ApplyResult(.alreadyAppliedToday) }

        if force {
            state.manualChanges.removeAll { now.timeIntervalSince($0) >= Self.manualWindow }
            if let next = Self.manualChangeAvailableAt(state, now: now) {
                return ApplyResult(.rateLimited, error:
                    "You can use Change now \(Self.maxManualChangesPerHour) times an hour. Try again in \(Self.minutesUntil(next, now: now)) min.")
            }
        }

        // Only an automatic change moves the schedule; a manual one only counts toward its own limit.
        if !force { state.lastAttemptAt = now }
        do {
            let entries = try await applyCore(s)
            if force {
                state.manualChanges.append(clock.now)
            } else {
                state.lastAppliedDate = clock.today
                state.lastAppliedAt = clock.now
                state.lastError = nil
                state.consecutiveFailures = 0
                state.nextRetryAt = nil
            }
            settings.saveState(state)
            return ApplyResult(.applied, entries)
        } catch {
            let providerError = error as? ProviderError
            let message = (error as? LocalizedError)?.errorDescription ?? "Something went wrong: \(error.localizedDescription)"
            Log.wallpaper.warning("Wallpaper change failed: \(message, privacy: .public)")
            // A failed "Change now" is shown right away; it must not delay the automatic change.
            if !force {
                state.lastError = message
                state.consecutiveFailures += 1
                state.nextRetryAt = clock.now.addingTimeInterval(RetryBackoff.delay(afterFailures: state.consecutiveFailures))
                settings.saveState(state)
            }
            return ApplyResult(.failed, error: message, errorKind: providerError?.kind)
        }
    }

    /// Puts a photo from history back on the desktop. It becomes the newest entry.
    public func reapply(_ entry: HistoryEntry) throws -> HistoryEntry {
        guard FileManager.default.fileExists(atPath: entry.filePath) else {
            throw WallpaperError("That photo's file is gone from disk.")
        }
        let s = settings.load()
        try setOnDesktop(entry.fileURL, screenID: entry.screenID, settings: s)
        var touched = entry
        touched.appliedAt = clock.now
        touched.id = try history.add(touched)
        return touched
    }

    // MARK: - Pipeline

    struct Target {
        var index: Int
        /// nil: the same photo on every screen.
        var screenID: String?
        var size: PixelSize
    }

    private func applyCore(_ s: OrielSettings) async throws -> [HistoryEntry] {
        if s.favoritesOnly, let favorite = try applyFavorite(s) { return [favorite] }
        guard provider.isConfigured else {
            throw ProviderError(.missingKey, "This build of Oriel has no Unsplash access key.")
        }

        let screens = setter.screens()
        let targets = Self.planTargets(screens, s)
        let enabled = s.enabledQueries()
        let mood = s.matchTimeOfDay ? PhotoMood.mood(at: clock.now, calendar: clock.calendar, systemDark: isDarkMode()) : .any

        // 1. Pick and download everything first.
        var picked: [(target: Target, photo: PhotoCandidate, topic: String, file: URL)] = []
        var usedTopics = Set<String>()
        var pickedIDs = Set<String>()
        for target in targets {
            let (photo, topic) = try await findCandidate(target.size, enabled, usedTopics, pickedIDs, s, mood)
            usedTopics.insert(topic)
            pickedIDs.insert(photo.id)
            let bytes = try await provider.download(photo, size: target.size)
            guard Self.looksLikeJPEG(bytes) else {
                throw WallpaperError("The downloaded file isn't a valid image; your current wallpaper stays.")
            }
            let file = try save(photo, bytes, index: target.index, folder: paths.wallpaperFolder(s))
            picked.append((target, photo, topic, file))
        }

        // 2. The desktop.
        do {
            if s.fit == .span && screens.count > 1 {
                try setter.setSpanning(picked[0].file)
            } else if targets.count == 1 && targets[0].screenID == nil {
                try setter.setForAll(picked[0].file, fit: s.fit)
            } else {
                for p in picked { try setter.set(p.file, on: p.target.screenID!, fit: s.fit) }
            }
        } catch let error as WallpaperError {
            throw error
        } catch {
            throw WallpaperError("macOS refused to set the wallpaper: \(error.localizedDescription)")
        }

        // 3. Record and tell Unsplash.
        let now = clock.now
        var entries: [HistoryEntry] = []
        for p in picked {
            var entry = HistoryEntry(
                provider: p.photo.provider, photoID: p.photo.id, filePath: p.file.path(percentEncoded: false),
                thumbnailURL: p.photo.thumbnailURL.absoluteString, photoPageURL: p.photo.photoPageURL.absoluteString,
                photographerName: p.photo.photographerName, photographerURL: p.photo.photographerURL.absoluteString,
                description: p.photo.description, topic: p.topic, screenID: p.target.screenID, appliedAt: now)
            entry.id = try history.add(entry)
            entries.append(entry)
            await provider.trackUsage(p.photo)
        }
        Log.wallpaper.info("Applied \(entries.map(\.photoID).joined(separator: ", "), privacy: .public)")
        return entries
    }

    static func planTargets(_ screens: [ScreenInfo], _ s: OrielSettings) -> [Target] {
        guard !screens.isEmpty else { return [Target(index: 0, screenID: nil, size: PixelSize(width: 2560, height: 1600))] }
        if s.fit == .span && screens.count > 1 {
            let left = screens.map(\.frame.x).min()!, top = screens.map(\.frame.y).min()!
            let right = screens.map { $0.frame.x + $0.frame.width }.max()!
            let bottom = screens.map { $0.frame.y + $0.frame.height }.max()!
            return [Target(index: 0, screenID: nil, size: PixelSize(width: right - left, height: bottom - top))]
        }
        // A photo of its own on each screen, each sized for that screen.
        if s.photoPerMonitor && screens.count > 1 {
            return screens.enumerated().map { Target(index: $0.offset, screenID: $0.element.id, size: $0.element.size) }
        }
        // Same photo everywhere: sized for the biggest screen so nothing gets upscaled.
        let biggest = screens.max { $0.frame.width * $0.frame.height < $1.frame.width * $1.frame.height }!
        return [Target(index: 0, screenID: nil, size: biggest.size)]
    }

    private func findCandidate(_ size: PixelSize, _ enabled: [String], _ usedTopics: Set<String>, _ pickedIDs: Set<String>,
                               _ s: OrielSettings, _ mood: Mood) async throws -> (PhotoCandidate, String) {
        var tried = usedTopics
        var lastNoResults: ProviderError?
        for _ in 0..<Self.maxQueriesPerTarget {
            let topic = topics.pick(enabled, avoiding: tried)
            tried.insert(topic)
            let candidates: [PhotoCandidate]
            do {
                candidates = try await provider.random(ImageQuery(topic, targetSize: size))
            } catch let error as ProviderError where error.kind == .noResults {
                lastNoResults = error
                continue
            }
            for c in PhotoMood.order(candidates, mood) {
                if pickedIDs.contains(c.id) || history.wasUsed(provider: c.provider, photoID: c.id) { continue }
                if CandidateFilter.reject(c, target: size, settings: s) != nil { continue }
                return (c, topic)
            }
        }
        throw lastNoResults ?? ProviderError(.noResults,
            "Couldn't find a new photo that passes your filters. Try more interests or looser filters.")
    }

    /// A random favorite other than the one on screen, with no download. Nil when there are none.
    private func applyFavorite(_ s: OrielSettings) throws -> HistoryEntry? {
        let favorites = history.list().filter { $0.isFavorite && FileManager.default.fileExists(atPath: $0.filePath) }
        guard !favorites.isEmpty else { return nil }
        let current = setter.current(on: nil)?.path(percentEncoded: false)
        let pool = favorites.count > 1 ? favorites.filter { $0.filePath != current } : favorites
        return try reapply((pool.isEmpty ? favorites : pool).randomElement()!)
    }

    private func setOnDesktop(_ file: URL, screenID: String?, settings s: OrielSettings) throws {
        if let screenID, setter.screens().contains(where: { $0.id == screenID }) {
            try setter.set(file, on: screenID, fit: s.fit)
        } else if s.fit == .span && setter.screens().count > 1 {
            try setter.setSpanning(file)
        } else {
            try setter.setForAll(file, fit: s.fit)
        }
    }

    private func save(_ photo: PhotoCandidate, _ bytes: Data, index: Int, folder: URL) throws -> URL {
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let day = clock.today.description
        let id = photo.id.filter { $0.isLetter || $0.isNumber || $0 == "-" || $0 == "_" }
        let file = folder.appending(path: "\(day)_\(photo.provider)_\(id)\(index > 0 ? "_\(index)" : "").jpg")
        try bytes.write(to: file, options: .atomic)
        return file
    }

    static func looksLikeJPEG(_ bytes: Data) -> Bool {
        bytes.count > 1024 && bytes.starts(with: [0xFF, 0xD8])
    }
}
