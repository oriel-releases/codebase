import Foundation
import Synchronization

/// What a change is doing right now, for the "Finding a photo… → Downloading… → Changing… → Syncing…" words.
public enum ApplyPhase: Sendable, CaseIterable {
    case finding, downloading, changing
    /// Sharing the new photo with the account's other devices.
    case syncing
    /// Another device set a new photo; it is being fetched here.
    case receiving
    /// Nothing in progress any more.
    case done

    /// The words shown on Home's button, the change popup and the other buttons.
    public var text: String {
        switch self {
        case .finding: "Finding a photo…"
        case .downloading: "Downloading…"
        case .changing: "Changing…"
        case .syncing: "Syncing…"
        case .receiving: "Receiving…"
        case .done: ""
        }
    }

    public static let allWords = allCases.filter { $0 != .done }.map(\.text)
}

public enum ApplyStatus: Sendable, Equatable {
    case applied, alreadyAppliedToday, paused, failed, busy, signInRequired, rateLimited, takenByOtherDevice
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

    /// A failure worth retrying later (offline, rate limit, a bad answer, or something unexpected).
    public var isTransientFailure: Bool {
        status == .failed && (errorKind == nil || errorKind == .network || errorKind == .rateLimited || errorKind == .badResponse)
    }
}

/// Whether someone is signed in. Nothing works until they are.
public protocol AuthState: Sendable {
    var isSignedIn: Bool { get }
}

/// Shares a wallpaper that was just applied with the account's other devices. Never throws.
public protocol SyncPublishing: Sendable {
    func publish(_ entry: HistoryEntry) async
    /// The accent color chosen for the photo on screen, for the account's other devices.
    func publishColor(_ entry: HistoryEntry, _ color: String) async
}

/// "First device wins": true when this device takes the moment, false when another one did, nil when unknown.
public protocol SlotClaiming: Sendable {
    func tryClaim(_ slotId: String) async -> Bool?
}

/// Picks a photo, downloads it, puts it on the desktop, records it and shares it. Everything is downloaded before the
/// desktop changes, so a failure halfway never leaves a blank or half-changed desktop.
public actor WallpaperService {
    /// Topics tried per screen before giving up.
    static let maxQueriesPerTarget = 3
    /// "Change now" is limited to this many successful changes per hour (every install shares one Unsplash key).
    public static let maxManualChangesPerHour = 2
    static let manualWindow: TimeInterval = 3600
    /// Requests kept back for the scheduled wallpaper: searching stops a little early rather than starving it.
    static let searchReserve = 10
    /// Photos are never asked for wider than this. A 5K/6K Retina display would otherwise reject most Unsplash photos
    /// as too small, and every download would be enormous.
    public static let maxWidth = 3840

    private let provider: ImageProvider
    private let setter: WallpaperSetter
    private let history: HistoryStore
    private let settings: SettingsStore
    private let topics: TopicPicker
    private let clock: OrielClock
    private let paths: AppPaths
    private let auth: AuthState?
    private let sync: SyncPublishing?
    private let slots: SlotClaiming?
    private let ai: PollinationsClient?
    private let accentSampler: AccentSampler?
    private let accentSetter: AccentSetting?
    private let isDarkMode: @Sendable () -> Bool

    /// After a successful change from any trigger in this process.
    public nonisolated let applied = Broadcaster<ApplyResult>()
    /// Every step of every change, from whatever task is working.
    public nonisolated let phases = Broadcaster<ApplyPhase>()
    /// After the accent color chosen for the photo on screen changed (here or on another device).
    public nonisolated let accentChosen = Broadcaster<Void>()

    /// The app (a long-running process) turns this on: photos for the next changes are prepared in the background, and
    /// network work that doesn't change the screen runs after the change. Off for one-shot command-line runs.
    public var prefetchEnabled = false

    private nonisolated let publishing = Counter()
    private nonisolated let afterQueue = SerialQueue()
    private nonisolated let publishQueue = SerialQueue()
    private nonisolated let queueLock = NSLock()
    private var prefetching = false

    public init(provider: ImageProvider, setter: WallpaperSetter, history: HistoryStore, settings: SettingsStore,
                topics: TopicPicker = RandomTopicPicker(), clock: OrielClock = SystemClock(), paths: AppPaths,
                auth: AuthState? = nil, sync: SyncPublishing? = nil, slots: SlotClaiming? = nil,
                ai: PollinationsClient? = nil, accentSampler: AccentSampler? = nil, accentSetter: AccentSetting? = nil,
                isDarkMode: @escaping @Sendable () -> Bool = { false }) {
        self.provider = provider
        self.setter = setter
        self.history = history
        self.settings = settings
        self.topics = topics
        self.clock = clock
        self.paths = paths
        self.auth = auth
        self.sync = sync
        self.slots = slots
        self.ai = ai
        self.accentSampler = accentSampler
        self.accentSetter = accentSetter
        self.isDarkMode = isDarkMode
    }

    public func setPrefetchEnabled(_ on: Bool) { prefetchEnabled = on }

    /// When "Change now" works again, or nil if it is available right now.
    public static func manualChangeAvailableAt(_ state: OrielState, now: Date) -> Date? {
        let recent = state.manualChanges.filter { now.timeIntervalSince($0) < manualWindow }.sorted()
        guard recent.count >= maxManualChangesPerHour else { return nil }
        return recent[recent.count - maxManualChangesPerHour].addingTimeInterval(manualWindow)
    }

    /// Whole minutes left until `date`, rounded up, at least 1 (never "0 min").
    public static func minutesUntil(_ date: Date, now: Date) -> Int {
        max(1, Int((date.timeIntervalSince(now) / 60).rounded(.up)))
    }

    static let signInRequired = ApplyResult(.signInRequired, error: "Sign in to Oriel first.")
    static let busy = ApplyResult(.busy, error: "Another change is already in progress.")

    // MARK: - The scheduled change and "Change now"

    /// - Parameter force: change even if this tick's wallpaper was already set ("Change now").
    public func apply(force: Bool) async -> ApplyResult {
        let result = await applyNow(force: force)
        finishPhase()
        return result
    }

    private func applyNow(force: Bool) async -> ApplyResult {
        if let auth, !auth.isSignedIn { return Self.signInRequired }
        let s = settings.load()
        if !force && s.paused { return ApplyResult(.paused) }
        // Following a friend: their wallpaper is used instead of the schedule (see FollowService).
        if !force && s.isFollowing { return ApplyResult(.paused) }
        if !force && ScheduleRules.isFresh(s, settings.loadState(), clock.now) { return ApplyResult(.alreadyAppliedToday) }

        // The app and a launchd run can fire at the same moment.
        guard let lock = ApplyLock.tryAcquire(paths.lockFile) else { return Self.busy }
        defer { withExtendedLifetime(lock) {} }

        // Re-check under the lock; the other process may have just finished.
        var state = settings.loadState()
        if !force && ScheduleRules.isFresh(s, state, clock.now) { return ApplyResult(.alreadyAppliedToday) }

        // "First device wins": every device ticks at the same moment; only the one that claims it picks a photo.
        if !force, let slots {
            let slot = ScheduleRules.slotId(s, ScheduleRules.dueTick(s, clock.now))
            if await slots.tryClaim(slot) == false {
                // Done for this tick: the other device's photo comes through sync/current.
                let now = clock.now
                settings.updateState {
                    $0.scheduleStartedAt = now
                    $0.nextRetryAt = nil
                }
                return ApplyResult(.takenByOtherDevice)
            }
        }

        if force {
            let now = clock.now
            state.manualChanges.removeAll { now.timeIntervalSince($0) >= Self.manualWindow }
            if let next = Self.manualChangeAvailableAt(state, now: now) {
                return ApplyResult(.rateLimited, error:
                    "You can use Change now twice an hour. Try again in \(Self.minutesUntil(next, now: now)) min.")
            }
        }

        // The schedule belongs to the app and "Change now" to the user: only an automatic change moves the schedule;
        // a manual one only counts toward its own limit.
        // Saved field by field on the state as it is then (the syncs save it meanwhile too).
        let manual = state.manualChanges
        if !force {
            let attempt = clock.now
            settings.updateState { $0.lastAttemptAt = attempt }
        }
        do {
            let entries = try await applyCore(s)
            let now = clock.now, today = clock.today
            settings.updateState { st in
                if force {
                    st.manualChanges = manual + [now]
                } else {
                    st.lastAppliedDate = today
                    st.lastAppliedAt = now
                    st.lastError = nil
                    st.consecutiveFailures = 0
                    st.nextRetryAt = nil
                }
            }

            // Let the account's other devices show the same photo. Never fails the change.
            if let first = entries.first { await publishSoon(first) }
            let result = ApplyResult(.applied, entries)
            applied.send(result)
            schedulePrefetch()
            return result
        } catch {
            let kind = (error as? ProviderError)?.kind
            let message = (error is ProviderError || error is WallpaperError)
                ? (error as! LocalizedError).errorDescription!
                : "Something went wrong: \(error.localizedDescription)"
            Log.wallpaper.warning("Wallpaper change failed (\(kind.map { "\($0)" } ?? "unknown"))", error)
            // A failed "Change now" is shown right away; it must not delay the automatic change.
            if !force {
                let now = clock.now
                settings.updateState { st in
                    st.lastError = message
                    st.consecutiveFailures += 1
                    st.nextRetryAt = now.addingTimeInterval(RetryBackoff.delay(afterFailures: st.consecutiveFailures))
                }
            }
            return ApplyResult(.failed, error: message, errorKind: kind)
        }
    }

    // MARK: - Search, covers, one chosen photo

    /// Photos for the words, most relevant first. Only ones big enough for the screen, so they look sharp.
    public func search(_ query: String, page: Int) async throws -> SearchPage {
        guard provider.isConfigured else { throw ProviderError(.missingKey, "This build of Oriel has no Unsplash access key.") }
        if let left = await provider.remainingRequests, left < Self.searchReserve {
            throw ProviderError(.rateLimited,
                "Searching is paused for a while so your daily wallpaper still gets its photos. Try again in the next hour.")
        }
        let size = Self.planTargets(setter.screens(), settings.load(), perMonitor: false)[0].size
        var found = try await provider.search(query, page: page)
        found.photos = found.photos.filter { $0.isLandscape && $0.width >= size.width && $0.height >= size.height }
        return found
    }

    /// A photo for the tile of one of your own interests: the top search result's thumbnail. Only with requests to
    /// spare (nil otherwise, to try later).
    public func findCover(_ query: String) async -> String? {
        guard provider.isConfigured else { return nil }
        if let left = await provider.remainingRequests, left < Self.searchReserve { return nil }
        guard let found = try? await provider.search(query, page: 1) else { return nil }
        return (found.photos.first(where: \.isLandscape) ?? found.photos.first)?.thumbnailURL.absoluteString
    }

    /// A photo someone shared with a link (or another device set), looked up by its Unsplash ID.
    public func sharedPhoto(_ id: String) async throws -> PhotoCandidate {
        guard provider.isConfigured else { throw ProviderError(.missingKey, "This build of Oriel has no Unsplash access key.") }
        return try await provider.photo(id: id)
    }

    /// Sets one specific photo (a Search result, a shared link, Wallpaper of the day, a photo from another device).
    /// `publish` shares it with the account's other devices; a photo that came from one of them isn't sent back.
    public func applyPhoto(_ photo: PhotoCandidate, topic: String? = nil, publish: Bool = true) async -> ApplyResult {
        let result = await applyPhotoNow(photo, topic: topic, publish: publish)
        finishPhase()
        return result
    }

    /// The size one picture for every screen is made for (the biggest screen, or all of them when spanned).
    public func screenSize() -> PixelSize {
        Self.planTargets(setter.screens(), settings.load(), perMonitor: false)[0].size
    }

    /// Sets a picture Oriel already has in hand: an AI wallpaper from the AI page (shared with the account's other
    /// devices through its public link), or one another device made.
    public func applyCreated(_ photo: PhotoCandidate, image: Data, topic: String = "created", publish: Bool = true) async -> ApplyResult {
        let result = await applyPhotoNow(photo, topic: topic, publish: publish, image: image)
        finishPhase()
        return result
    }

    /// An AI wallpaper made on another device (or a favorite of one): downloaded from its public link, then set.
    public func applyLinked(_ photo: PhotoCandidate, topic: String, publish: Bool) async -> ApplyResult {
        guard let ai else { return ApplyResult(.failed, error: "This build of Oriel can't show AI wallpapers.") }
        let image: Data
        do {
            phases.send(.downloading)
            image = try await ai.download(photo)
        } catch {
            finishPhase()
            return ApplyResult(.failed, error: error.localizedDescription, errorKind: (error as? ProviderError)?.kind)
        }
        return await applyCreated(photo, image: image, topic: topic, publish: publish)
    }

    private func applyPhotoNow(_ photo: PhotoCandidate, topic: String?, publish: Bool, image: Data? = nil) async -> ApplyResult {
        if let auth, !auth.isSignedIn { return Self.signInRequired }
        let s = settings.load()
        guard let lock = ApplyLock.tryAcquire(paths.lockFile) else { return Self.busy }
        defer { withExtendedLifetime(lock) {} }
        do {
            let size = Self.planTargets(setter.screens(), s, perMonitor: false)[0].size
            let bytes: Data
            if let image {
                bytes = image
            } else {
                phases.send(.downloading)
                bytes = try await provider.download(photo, size: size)
            }
            guard image == nil ? Self.looksLikeJPEG(bytes) : PollinationsClient.isImage(bytes) else {
                throw WallpaperError("Downloaded file isn't a valid image; keeping your current wallpaper.")
            }
            let file = try save(photo, bytes, index: 0, folder: paths.wallpaperFolder(s),
                                ext: PollinationsClient.isPNG(bytes) ? "png" : "jpg")
            phases.send(.changing)
            try setForAll(file, s)
            matchAccent(photo.id, file)
            let entry = try record(photo, file: file, topic: topic ?? "search", screenID: nil)
            // Only Unsplash counts downloads; an AI picture has nobody to tell.
            if image == nil { afterChange { [provider] in await provider.trackUsage(photo) } }

            // Counts as this tick's change, so the scheduler doesn't replace it right away.
            let now = clock.now, today = clock.today
            settings.updateState { st in
                st.lastAppliedDate = today
                st.lastAppliedAt = now
                st.lastError = nil
                st.consecutiveFailures = 0
                st.nextRetryAt = nil
            }

            if publish { await publishSoon(entry) }
            let result = ApplyResult(.applied, [entry])
            applied.send(result)
            return result
        } catch {
            let kind = (error as? ProviderError)?.kind
            let message = (error is ProviderError || error is WallpaperError)
                ? (error as! LocalizedError).errorDescription!
                : "Something went wrong: \(error.localizedDescription)"
            Log.wallpaper.warning("Setting the chosen wallpaper failed", error)
            return ApplyResult(.failed, error: message, errorKind: kind)
        }
    }

    /// Puts a photo from History back on the desktop; it becomes the newest entry. A favorite from another device (or
    /// a file cleaned up since) is downloaded again.
    public func reapply(_ entry: HistoryEntry) async throws {
        if !Self.fileExists(entry.filePath) && entry.provider == PollinationsClient.provider,
           let made = PollinationsClient.fromLink(entry.thumbnailURL, prompt: entry.description) {
            let result = await applyLinked(made, topic: entry.topic ?? "favorite", publish: true)
            if result.status != .applied { throw WallpaperError(result.error ?? "Couldn't set it. Try again.") }
            return
        }
        if !Self.fileExists(entry.filePath) && entry.provider == "unsplash" {
            let photo = try await sharedPhoto(entry.photoID)
            let result = await applyPhoto(photo, topic: entry.topic ?? "favorite")
            if result.status != .applied { throw WallpaperError(result.error ?? "Couldn't set it. Try again.") }
            return
        }
        defer { finishPhase() }
        phases.send(.changing)
        let touched = try reapplyCore(entry)
        // History is another way to change the wallpaper: share it like the rest.
        await publishSoon(touched)
        applied.send(ApplyResult(.applied, [touched]))
    }

    private func reapplyCore(_ entry: HistoryEntry) throws -> HistoryEntry {
        guard Self.fileExists(entry.filePath) else { throw WallpaperError("That image file is gone from disk.") }
        let s = settings.load()
        if let screenID = entry.screenID, setter.screens().contains(where: { $0.id == screenID }) {
            try wrapSetterError { try setter.set(entry.fileURL, on: screenID, fit: s.fit) }
        } else {
            try setForAll(entry.fileURL, s)
        }
        matchAccent(entry.photoID, entry.fileURL)
        var touched = entry
        touched.appliedAt = clock.now
        touched.id = try history.add(touched)
        return touched
    }

    // MARK: - Pipeline

    struct Target: Sendable {
        var index: Int
        /// nil: the same photo on every screen.
        var screenID: String?
        var size: PixelSize
    }

    private func applyCore(_ s: OrielSettings) async throws -> [HistoryEntry] {
        phases.send(.finding)
        if s.favoritesOnly, let favorite = try applyFavorite() { return [favorite] }
        guard provider.isConfigured else {
            throw ProviderError(.missingKey, "This build of Oriel has no Unsplash access key.")
        }

        let screens = setter.screens()
        let targets = Self.planTargets(screens, s)
        let enabled = s.enabledQueries()
        let mood = moodAt(clock.now, s)

        // 1. Pick and download everything first.
        var picked: [(target: Target, photo: PhotoCandidate, topic: String, file: URL)] = []
        var usedTopics = Set<String>()
        var pickedIDs = Set<String>()
        for target in targets {
            // A photo prepared ahead of time, when one still fits: no search, no download.
            if targets.count == 1, let ready = takePrefetch(target.size, enabled, s, mood) {
                picked.append((target, ready.photo, ready.topic, ready.file))
                Log.wallpaper.info("Using the photo prepared ahead of time: \(ready.photo.id)")
                continue
            }
            let (photo, topic) = try await findCandidate(target.size, enabled, usedTopics, pickedIDs, s, mood)
            usedTopics.insert(topic.lowercased())
            pickedIDs.insert(photo.id)
            phases.send(.downloading)
            let bytes = try await provider.download(photo, size: target.size)
            guard Self.looksLikeJPEG(bytes) else {
                throw WallpaperError("Downloaded file isn't a valid image; keeping your current wallpaper.")
            }
            let file = try save(photo, bytes, index: target.index, folder: paths.wallpaperFolder(s))
            picked.append((target, photo, topic, file))
        }

        // 2. The desktop.
        phases.send(.changing)
        if targets.count == 1 && targets[0].screenID == nil {
            try setForAll(picked[0].file, s)
        } else {
            for p in picked { try wrapSetterError { try setter.set(p.file, on: p.target.screenID!, fit: s.fit) } }
        }
        matchAccent(picked[0].photo.id, picked[0].file)

        // 3. Record, then tell Unsplash (after the change).
        var entries: [HistoryEntry] = []
        for p in picked {
            entries.append(try record(p.photo, file: p.file, topic: p.topic, screenID: p.target.screenID))
            let photo = p.photo
            afterChange { [provider] in await provider.trackUsage(photo) }
        }
        Log.wallpaper.info("Applied \(entries.count) wallpaper(s): \(entries.map(\.photoID).joined(separator: ", "))")
        return entries
    }

    /// - Parameter perMonitor: false for one photo chosen by the user: then it is sized for the biggest screen.
    static func planTargets(_ screens: [ScreenInfo], _ s: OrielSettings, perMonitor: Bool = true) -> [Target] {
        guard !screens.isEmpty else { return [Target(index: 0, screenID: nil, size: PixelSize(width: 1920, height: 1080))] }
        if s.fit == .span && screens.count > 1 {
            let left = screens.map(\.frame.x).min()!, top = screens.map(\.frame.y).min()!
            let right = screens.map { $0.frame.x + $0.frame.width }.max()!
            let bottom = screens.map { $0.frame.y + $0.frame.height }.max()!
            return [Target(index: 0, screenID: nil, size: capped(PixelSize(width: right - left, height: bottom - top)))]
        }
        // A photo of its own on each screen, each sized for that screen.
        if perMonitor && s.photoPerMonitor && screens.count > 1 {
            return screens.enumerated().map { Target(index: $0.offset, screenID: $0.element.id, size: capped($0.element.size)) }
        }
        // Same photo everywhere: sized for the biggest screen so nothing gets upscaled.
        let biggest = screens.max { $0.frame.width * $0.frame.height < $1.frame.width * $1.frame.height }!
        return [Target(index: 0, screenID: nil, size: capped(biggest.size))]
    }

    /// At most `maxWidth` wide, keeping the screen's shape.
    static func capped(_ size: PixelSize) -> PixelSize {
        guard size.width > maxWidth else { return size }
        return PixelSize(width: maxWidth, height: Int((Double(size.height) * Double(maxWidth) / Double(size.width)).rounded()))
    }

    private func findCandidate(_ size: PixelSize, _ enabled: [String], _ usedTopics: Set<String>, _ pickedIDs: Set<String>,
                               _ s: OrielSettings, _ mood: Mood) async throws -> (PhotoCandidate, String) {
        var tried = usedTopics
        var lastNoResults: ProviderError?
        for _ in 0..<Self.maxQueriesPerTarget {
            let topic = topics.pick(enabled, avoiding: Set(enabled.filter { tried.contains($0.lowercased()) }))
            tried.insert(topic.lowercased())
            let candidates: [PhotoCandidate]
            do {
                candidates = try await provider.random(ImageQuery(topic, targetSize: size))
            } catch let error as ProviderError where error.kind == .noResults {
                lastNoResults = error
                continue
            }
            // Photos that suit the moment (dark in the evening, bright in the morning) come first.
            for c in PhotoMood.order(candidates, mood) {
                if pickedIDs.contains(c.id) || history.wasUsed(provider: c.provider, photoID: c.id) { continue }
                if CandidateFilter.reject(c, target: size, settings: s) != nil { continue }
                return (c, topic)
            }
        }
        throw lastNoResults ?? ProviderError(.noResults,
            "Couldn't find a new photo that passes your filters. Try more topics or looser filters.")
    }

    /// A random favorite other than the one on screen, with no download. Nil when there are none with a file.
    private func applyFavorite() throws -> HistoryEntry? {
        let favorites = history.list().filter { $0.isFavorite && Self.fileExists($0.filePath) }
        guard !favorites.isEmpty else { return nil }
        phases.send(.changing)
        let current = setter.current(on: nil)?.standardizedFileURL.path(percentEncoded: false)
        let pool = favorites.count > 1 ? favorites.filter { $0.fileURL.standardizedFileURL.path(percentEncoded: false) != current } : favorites
        let pick = (pool.isEmpty ? favorites : pool).randomElement()!
        let entry = try reapplyCore(pick)
        Log.wallpaper.info("Applied favorite \(pick.photoID)")
        return entry
    }

    private func setForAll(_ file: URL, _ s: OrielSettings) throws {
        if s.fit == .span && setter.screens().count > 1 {
            try wrapSetterError { try setter.setSpanning(file) }
        } else {
            try wrapSetterError { try setter.setForAll(file, fit: s.fit) }
        }
    }

    private func wrapSetterError(_ body: () throws -> Void) throws {
        do {
            try body()
        } catch let error as WallpaperError {
            throw error
        } catch {
            throw WallpaperError("macOS refused to set the wallpaper: \(error.localizedDescription)")
        }
    }

    private func record(_ photo: PhotoCandidate, file: URL, topic: String, screenID: String?) throws -> HistoryEntry {
        var entry = HistoryEntry(
            provider: photo.provider, photoID: photo.id, filePath: file.path(percentEncoded: false),
            thumbnailURL: photo.thumbnailURL.absoluteString, photoPageURL: photo.photoPageURL.absoluteString,
            photographerName: photo.photographerName, photographerURL: photo.photographerURL.absoluteString,
            description: photo.description, topic: topic, screenID: screenID, appliedAt: clock.now)
        entry.id = try history.add(entry)
        return entry
    }

    private func save(_ photo: PhotoCandidate, _ bytes: Data, index: Int, folder: URL, ext: String = "jpg") throws -> URL {
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let file = folder.appending(path: fileName(photo, index: index, ext: ext))
        let part = file.appendingPathExtension("part")
        try bytes.write(to: part)
        _ = try? FileManager.default.removeItem(at: file)
        try FileManager.default.moveItem(at: part, to: file)
        return file
    }

    /// "2026-10-14_unsplash_FIKD9t5_5zQ.jpg", "2026-10-20_ai_ai-e774….png"
    private func fileName(_ photo: PhotoCandidate, index: Int, ext: String = "jpg") -> String {
        "\(clock.today)_\(photo.provider)_\(Self.sanitize(photo.id))\(index > 0 ? "_\(index)" : "").\(ext)"
    }

    static func sanitize(_ s: String) -> String {
        s.filter { ($0.isASCII && ($0.isLetter || $0.isNumber)) || $0 == "-" || $0 == "_" }
    }

    static func looksLikeJPEG(_ bytes: Data) -> Bool {
        bytes.count > 1024 && bytes.starts(with: [0xFF, 0xD8])
    }

    static func fileExists(_ path: String) -> Bool {
        !path.isEmpty && FileManager.default.fileExists(atPath: path)
    }

    private func moodAt(_ date: Date, _ s: OrielSettings) -> Mood {
        s.matchTimeOfDay ? PhotoMood.mood(at: date, calendar: clock.calendar, systemDark: isDarkMode()) : .any
    }

    // MARK: - Choosing the accent color

    /// The colors offered for a photo on Home: its top three, best first (empty when it has no file).
    public nonisolated func accentOptions(_ entry: HistoryEntry) -> [String] {
        guard let accentSampler, Self.fileExists(entry.filePath) else { return [] }
        return accentSampler.sampleTop(entry.filePath)
    }

    /// The accent color chosen for the photo, or nil when Oriel picks it.
    public nonisolated func chosenAccent(_ photoId: String?) -> String? {
        let state = settings.loadState()
        guard let id = state.accentPhotoId, let hex = state.accentColor, id == photoId else { return nil }
        return hex
    }

    /// The user chose one of the photo's colors: it becomes the accent on the account's other devices. macOS has no
    /// way for an app to set the system accent, so (like Linux) nothing changes here but the choice.
    public func chooseAccent(_ entry: HistoryEntry, _ hex: String) async {
        rememberAccent(entry.photoID, hex)
        // Like Windows: choosing a color is asking for it, so "Match accent color" turns on.
        if let accentSetter {
            if !settings.load().matchAccentColor { settings.update { $0.matchAccentColor = true } }
            accentSetter.apply(hex)
        }
        await sync?.publishColor(entry, hex.uppercased())
    }

    /// Another device chose this color for the photo on screen: used here too (when the accent follows the photo).
    public nonisolated func receiveAccent(_ photoId: String, _ hex: String) {
        rememberAccent(photoId, hex)
        if let accentSetter, settings.load().matchAccentColor { accentSetter.apply(hex) }
    }

    /// The accent of the wallpaper on screen now ("Match accent color" was just turned on).
    public nonisolated func applyAccentToCurrent() {
        guard let current = history.list().first(where: { Self.fileExists($0.filePath) }) else { return }
        matchAccent(current.photoID, current.fileURL)
    }

    /// The photo's accent on the Mac, when "Match accent color" is on: the color chosen for it (here or on another
    /// device), else the one Oriel picks from it. Right after the desktop changed (it's quick: one small decode).
    private nonisolated func matchAccent(_ photoId: String, _ file: URL) {
        guard let accentSetter, settings.load().matchAccentColor else { return }
        guard let hex = chosenAccent(photoId) ?? accentSampler?.sample(file.path(percentEncoded: false)) else { return }
        accentSetter.apply(hex)
    }

    /// Keeps the color for the photo (before it's set, so setting it uses this color).
    public nonisolated func rememberAccent(_ photoId: String, _ hex: String) {
        let state = settings.loadState()
        if state.accentPhotoId == photoId && state.accentColor?.caseInsensitiveCompare(hex) == .orderedSame { return }
        settings.updateState {
            $0.accentPhotoId = photoId
            $0.accentColor = hex.uppercased()
        }
        accentChosen.send(())
    }

    // MARK: - After the change, and publishing

    /// Network work that doesn't change what's on screen. In the app it runs after the change, one at a time in order,
    /// and never fails it. A one-shot run waits for it, as it is about to exit.
    private func afterChange(_ work: @escaping @Sendable () async -> Void) {
        if prefetchEnabled {
            afterQueue.add(work)
        } else {
            let done = Task { await work() }
            pendingInline.append(done)
        }
    }

    private var pendingInline: [Task<Void, Never>] = []

    /// Done once everything queued after changes so far has finished (the command line waits for it before exiting).
    public func afterChangeDone() async {
        await afterQueue.drain()
        await publishQueue.drain()
        let inline = pendingInline
        pendingInline = []
        for task in inline { await task.value }
    }

    /// Shares a new wallpaper with the account's other devices on its own queue, in order. Shows "Syncing…" until it's
    /// done. Never fails the change.
    private func publishSoon(_ entry: HistoryEntry) async {
        guard let sync else { return }
        publishing.add(1)
        phases.send(.syncing)
        let work: @Sendable () async -> Void = { [phases, publishing] in
            await sync.publish(entry)
            if publishing.add(-1) == 0 { phases.send(.done) }
        }
        if prefetchEnabled {
            publishQueue.add(work)
        } else {
            await work()
        }
    }

    /// Shown by FollowService while it fetches a photo another device set.
    public nonisolated func reportPhase(_ phase: ApplyPhase) { phases.send(phase) }

    /// A change ended: "Done", unless its photo is still being shared ("Syncing…" ends that one).
    public nonisolated func finishPhase() {
        if publishing.value == 0 { phases.send(.done) }
    }

    // MARK: - The next wallpapers, ahead of time

    /// How many photos wait ready: "Change now" works twice an hour, so both changes are instant.
    static let prefetchDepth = maxManualChangesPerHour
    static let prefetchMaxAge: TimeInterval = 3 * 86_400

    struct Prefetched: Codable, Sendable {
        var photo: PhotoCandidate
        var topic: String
        var width: Int
        var height: Int
        var file: String
        var at: Date
    }

    private var queueFile: URL { paths.prefetchDir.appending(path: "queue.json") }

    private func schedulePrefetch() {
        guard prefetchEnabled else { return }
        Task {
            try? await Task.sleep(for: .seconds(10)) // the change itself (and what follows it) comes first
            await self.prefetch()
        }
    }

    /// Tops up the photos prepared ahead of time: finds and downloads them, without touching the desktop.
    public func prefetch() async {
        guard prefetchEnabled, !prefetching else { return }
        if let auth, !auth.isSignedIn { return }
        prefetching = true
        defer { prefetching = false }
        do {
            let s = settings.load()
            if s.favoritesOnly || s.isFollowing || !provider.isConfigured { return }
            let targets = Self.planTargets(setter.screens(), s)
            guard targets.count == 1 else { return }
            let size = targets[0].size
            forgetStrayFiles()

            while true {
                let queue = readQueue()
                if queue.count >= Self.prefetchDepth { return }
                // Only with requests to spare: never at the cost of the scheduled change or a search.
                if let left = await provider.remainingRequests, left < Self.searchReserve * 2 { return }

                // The first suits right now (the next "Change now"), the second the next scheduled change.
                let at = queue.isEmpty ? clock.now : nextChangeAt(s)
                let (photo, topic) = try await findCandidate(size, s.enabledQueries(),
                    Set(queue.map { $0.topic.lowercased() }), Set(queue.map(\.photo.id)), s, moodAt(at, s))
                let bytes = try await provider.download(photo, size: size)
                guard Self.looksLikeJPEG(bytes) else { return }

                try FileManager.default.createDirectory(at: paths.prefetchDir, withIntermediateDirectories: true)
                let file = paths.prefetchDir.appending(path: "\(Self.sanitize(photo.id)).jpg")
                let part = file.appendingPathExtension("part")
                try bytes.write(to: part)
                _ = try? FileManager.default.removeItem(at: file)
                try FileManager.default.moveItem(at: part, to: file)
                // Only listed once it is whole on disk, so whatever is in the queue can be used right away.
                queueLock.withLock {
                    var now = readQueue()
                    now.append(Prefetched(photo: photo, topic: topic, width: size.width, height: size.height,
                                          file: file.path(percentEncoded: false), at: clock.now))
                    writeQueue(now)
                }
                Log.wallpaper.info("Prepared a wallpaper ahead of time: \(photo.id) (\(queue.count + 1) ready)")
            }
        } catch {
            Log.wallpaper.info("Couldn't prepare the next wallpaper: \(error.localizedDescription)")
        }
    }

    /// A prepared photo that still fits (same size, a topic that is still on, the filters, not used since), moved into
    /// the wallpaper folder. One that suits the moment comes first, else any. Ones that no longer fit are thrown away.
    private func takePrefetch(_ size: PixelSize, _ enabled: [String], _ s: OrielSettings, _ mood: Mood) -> (photo: PhotoCandidate, topic: String, file: URL)? {
        queueLock.withLock {
            let queue = readQueue()
            guard !queue.isEmpty else { return nil }
            let on = Set(enabled.map { $0.lowercased() })
            var usable: [Prefetched] = []
            for ready in queue {
                let fits = FileManager.default.fileExists(atPath: ready.file)
                    && ready.width == size.width && ready.height == size.height
                    && clock.now.timeIntervalSince(ready.at) < Self.prefetchMaxAge
                    && (enabled.isEmpty ? ready.topic == RandomTopicPicker.fallback : on.contains(ready.topic.lowercased()))
                    && CandidateFilter.reject(ready.photo, target: size, settings: s) == nil
                    && !history.wasUsed(provider: ready.photo.provider, photoID: ready.photo.id)
                if fits { usable.append(ready) } else { try? FileManager.default.removeItem(atPath: ready.file) }
            }
            let index = usable.firstIndex { PhotoMood.fits($0.photo, mood) } ?? usable.indices.first
            guard let index else {
                writeQueue([])
                return nil
            }
            let pick = usable.remove(at: index)
            writeQueue(usable)
            // Into the wallpaper folder, named like any other wallpaper.
            let folder = paths.wallpaperFolder(s)
            do {
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let file = folder.appending(path: fileName(pick.photo, index: 0))
                _ = try? FileManager.default.removeItem(at: file)
                try FileManager.default.moveItem(at: URL(filePath: pick.file), to: file)
                return (pick.photo, pick.topic, file)
            } catch {
                return nil
            }
        }
    }

    private func readQueue() -> [Prefetched] {
        guard let data = try? Data(contentsOf: queueFile) else { return [] }
        return (try? JSONSettingsStore.decoder.decode([Prefetched].self, from: data)) ?? []
    }

    private func writeQueue(_ queue: [Prefetched]) {
        try? FileManager.default.createDirectory(at: paths.prefetchDir, withIntermediateDirectories: true)
        try? JSONSettingsStore.encoder.encode(queue).write(to: queueFile, options: .atomic)
    }

    /// Photos no longer in the queue (half-written ones).
    private func forgetStrayFiles() {
        queueLock.withLock {
            guard let files = try? FileManager.default.contentsOfDirectory(at: paths.prefetchDir, includingPropertiesForKeys: nil) else { return }
            let listed = Set(readQueue().map { URL(filePath: $0.file).lastPathComponent })
            for file in files where file.lastPathComponent != "queue.json" && !listed.contains(file.lastPathComponent) {
                try? FileManager.default.removeItem(at: file)
            }
        }
    }

    /// Roughly when the next scheduled change happens, to pick a photo that suits that moment.
    private func nextChangeAt(_ s: OrielSettings) -> Date {
        if let interval = s.interval { return clock.now.addingTimeInterval(interval) }
        return ScheduleRules.nextTick(s, clock.now)
    }
}
