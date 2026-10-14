import AppKit
import Observation
import OrielKit

/// The sign-in screen: it opens the website, which comes back with an oriel://auth link.
@MainActor
@Observable
final class SignInModel {
    private let sp: Services
    weak var app: AppModel?

    private(set) var isWaiting = false
    private(set) var isCompleting = false
    var error: String?

    init(services: Services) {
        sp = services
    }

    func google() { openWebsite(OrielLinks.login) }
    func email() { openWebsite(OrielLinks.loginEmail) }

    private func openWebsite(_ link: String) {
        error = nil
        if NSWorkspace.shared.open(URL(string: link)!) {
            isWaiting = true
        } else {
            error = "Couldn't open your browser. Go to oriel.akbarjon.uz/login yourself."
        }
    }

    func complete(googleToken: String) async {
        await complete { try await self.sp.auth.signIn(googleIdToken: googleToken) }
    }

    func complete(refreshToken: String) async {
        await complete { try await self.sp.auth.signIn(refreshToken: refreshToken) }
    }

    private func complete(_ signIn: @escaping () async throws -> Void) async {
        guard !isCompleting, !sp.auth.isSignedIn else { return }
        isCompleting = true
        error = nil
        do {
            try await signIn()
        } catch let e as FirebaseError {
            error = switch e.failure {
            case .network: e.message
            case .rejected, .notFound: "That sign-in expired or isn't valid. Sign in again from the website."
            case .other: "Couldn't sign you in right now. Please try again."
            }
            Log.auth.warning("Sign-in failed: \(e.message)")
        } catch {
            self.error = "Couldn't sign you in right now. Please try again."
        }
        isCompleting = false
    }

    func invalid() {
        error = "That sign-in link wasn't valid. Sign in again from the website."
    }

    func finished() {
        isWaiting = false
        isCompleting = false
        error = nil
    }
}

/// The card for a photo someone shared with a link (`oriel://wallpaper?id=…&from=…`).
@MainActor
@Observable
final class SharedPhotoModel {
    enum Stage { case loading, ready, failed, done }

    private let sp: Services
    weak var app: AppModel?

    private(set) var isOpen = false
    private(set) var stage = Stage.loading
    private(set) var from = ""
    private(set) var photo: PhotoCandidate?
    private(set) var loadError = ""
    private(set) var setError: String?
    private(set) var words: String?
    private var version = 0

    init(services: Services) {
        sp = services
    }

    var heading: String {
        if stage == .done { return "It's your wallpaper now" }
        return from.isEmpty ? "Someone shared a wallpaper with you" : "\(from) shared a wallpaper with you"
    }

    func open(_ id: String, from: String) async {
        version += 1
        let mine = version
        isOpen = true
        self.from = from
        stage = .loading
        photo = nil
        setError = nil
        sp.stats.count(StatsReporter.sharesOpened)
        do {
            let found = try await sp.wallpapers.sharedPhoto(id)
            guard mine == version else { return }
            photo = found
            stage = .ready
        } catch {
            guard mine == version else { return }
            loadError = (error as? ProviderError)?.kind == .noResults ? "That photo isn't available any more." : error.localizedDescription
            stage = .failed
        }
    }

    func set() async {
        guard let photo, words == nil else { return }
        words = ApplyPhase.downloading.text
        setError = nil
        let result = await app?.watchPhases({ [sp] in await sp.wallpapers.applyPhoto(photo, topic: "shared") },
                                            report: { [weak self] w in self?.words = w })
        words = nil
        if result?.status == .applied {
            stage = .done
            sp.stats.count(StatsReporter.sharesSet)
        } else {
            setError = result?.error ?? "Couldn't set the wallpaper. Try again."
        }
        app?.reload()
    }

    func close() {
        version += 1
        isOpen = false
    }
}

/// Search: photos for any words, set with one click, or kept as an interest.
@MainActor
@Observable
final class SearchModel {
    struct Result: Identifiable, Equatable {
        var photo: PhotoCandidate
        var id: String { photo.id }
    }

    private let sp: Services
    weak var app: AppModel?

    var text = ""
    private(set) var query: String?
    private(set) var results: [Result] = []
    private(set) var isSearching = false
    private(set) var hasMore = false
    private(set) var ideas: [String] = []
    var banner: Banner?
    /// The result being set, and its words; the last one set shows ✓.
    private(set) var settingId: String?
    private(set) var settingWords: String?
    private(set) var setId: String?

    private var page = 0
    private var totalPages = 1
    private var version = 0
    private var lastRequest = Date.distantPast

    init(services: Services) {
        sp = services
        shuffle()
    }

    func shuffle() {
        ideas = Array(SearchIdeas.all.shuffled().prefix(8))
    }

    var isStart: Bool { query == nil }

    func submit() async {
        let words = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !words.isEmpty, !isSearching else { return }
        // The same words while results are on screen: nothing new to ask for.
        if words == query { return }
        version += 1
        query = words
        results = []
        page = 0
        totalPages = 1
        banner = nil
        await loadMore()
    }

    func search(_ idea: String) async {
        text = idea
        await submit()
    }

    /// The next page, and up to 3 pages in all until at least 6 photos passed the size filter.
    func loadMore() async {
        guard let query, !isSearching else { return }
        let mine = version
        isSearching = true
        defer { if mine == version { isSearching = false } }
        var found = 0
        var pages = 0
        do {
            while pages < 3 && found < 6 && page < totalPages {
                // At least 1.5 s between two requests: mashing Enter can't burn the hourly limit.
                let wait = 1.5 - Date().timeIntervalSince(lastRequest)
                if wait > 0 { try await Task.sleep(for: .seconds(wait)) }
                lastRequest = Date()
                let next = try await sp.wallpapers.search(query, page: page + 1)
                guard mine == version else { return }
                page += 1
                pages += 1
                totalPages = max(next.totalPages, 1)
                let known = Set(results.map(\.id))
                let fresh = next.photos.filter { !known.contains($0.id) }.map(Result.init)
                results += fresh
                found += fresh.count
            }
            hasMore = page < totalPages
        } catch {
            guard mine == version else { return }
            let key = [.missingKey, .unauthorized].contains((error as? ProviderError)?.kind)
            banner = Banner(severity: key ? .error : .warning, title: "Couldn't search", message: error.localizedDescription)
        }
    }

    /// ✕: back to the start, dropping anything still loading.
    func clear() {
        version += 1
        text = ""
        query = nil
        results = []
        hasMore = false
        isSearching = false
        banner = nil
        setId = nil
    }

    func set(_ result: Result) async {
        guard settingId == nil, let app else { return }
        settingId = result.id
        settingWords = ApplyPhase.downloading.text
        banner = nil
        let photo = result.photo
        let topic = query ?? "search"
        let r = await app.watchPhases({ [sp] in await sp.wallpapers.applyPhoto(photo, topic: topic) },
                                      report: { [weak self] w in self?.settingWords = w })
        settingId = nil
        settingWords = nil
        if r.status == .applied {
            setId = result.id
        } else {
            banner = Banner(severity: .warning, title: "Couldn't set the wallpaper", message: r.error ?? "Try again.")
        }
        app.reload()
    }

    var canAddToInterests: Bool {
        guard let query, let app else { return false }
        return !app.settings.topics.contains { $0.query.caseInsensitiveCompare(query) == .orderedSame && $0.enabled }
    }

    func addToInterests() {
        guard let query = query?.lowercased().trimmingCharacters(in: .whitespaces), let app else { return }
        if let i = app.settings.topics.firstIndex(where: { $0.query.caseInsensitiveCompare(query) == .orderedSame }) {
            app.settings.topics[i].enabled = true
        } else {
            app.settings.topics.append(TopicSetting(query: query, enabled: true, isCustom: true))
            Task { await findCovers() }
        }
    }

    // MARK: - Your own interests' covers

    private var coverTried = Set<String>()
    private(set) var findingCover: String?

    /// For each custom interest without a photo: one lookup per app run, one at a time.
    func findCovers() async {
        guard let app, findingCover == nil else { return }
        for topic in app.settings.topics where topic.isCustom && topic.coverURL == nil && !coverTried.contains(topic.query.lowercased()) {
            // The thumbnail of the last wallpaper from that interest will do.
            if let used = app.entries.first(where: { $0.topic?.caseInsensitiveCompare(topic.query) == .orderedSame }) {
                setCover(used.thumbnailURL, for: topic.query)
                continue
            }
            findingCover = topic.query
            let cover = await sp.wallpapers.findCover(topic.query)
            findingCover = nil
            if let cover {
                coverTried.insert(topic.query.lowercased())
                setCover(cover, for: topic.query)
            }
        }
    }

    private func setCover(_ url: String, for query: String) {
        guard let app, let i = app.settings.topics.firstIndex(where: { $0.query == query && $0.isCustom }) else { return }
        app.settings.topics[i].coverURL = url
    }

    /// The window closed: drop results and thumbnails.
    func release() {
        if !isSearching { results = [] ; query = nil; hasMore = false }
    }
}

/// Friends: share your wallpaper, follow a friend's.
@MainActor
@Observable
final class FriendsModel {
    private let sp: Services
    weak var app: AppModel?

    private(set) var followerCount: Int?
    private(set) var followers: [Follower] = []
    private(set) var loadingFollowers = false
    private(set) var sharing = false
    var linkCopied = false
    var banner: Banner?
    var codeText = "" {
        didSet {
            let formatted = FollowCodes.formatInput(codeText)
            if formatted != codeText { codeText = formatted }
        }
    }
    var codeError: String?
    private(set) var following = false
    var confirmRemove: String?

    init(services: Services) {
        sp = services
    }

    var linkCode: String? { sp.follow.linkCode }

    func reload() {
        if let error = sp.follow.linkError {
            banner = Banner(severity: .warning, title: "", message: error)
            sp.follow.clearLinkError()
        } else if let problem = sp.store.loadState().followProblem {
            banner = Banner(severity: .warning, title: "", message: problem)
        }
        if app?.settings.myFollowCode != nil { Task { await loadCount() } }
    }

    func loadCount() async {
        followerCount = await sp.follow.listFollowers()?.count
    }

    /// "Share": the follow link, through the share picker (copied when that can't open).
    func shareLink() async -> String? {
        guard !sharing else { return nil }
        sharing = true
        defer { sharing = false }
        do {
            let code = try await sp.follow.ensureCode()
            app?.reload()
            await loadCount()
            return ShareLink.forFollow(code)
        } catch {
            banner = Banner(severity: .warning, title: "", message: error.localizedDescription)
            return nil
        }
    }

    func stopSharing() async {
        do {
            try await sp.follow.stopSharing()
            followerCount = nil
        } catch {
            banner = Banner(severity: .warning, title: "", message: error.localizedDescription)
        }
    }

    var codeIsValid: Bool { FollowCodes.normalize(codeText) != nil }

    /// Follows with the typed code. True when it worked (the sheet closes).
    func follow() async -> Bool {
        guard codeIsValid, !following else { return false }
        following = true
        defer { following = false }
        do {
            try await sp.follow.follow(codeText)
            codeText = ""
            codeError = nil
            banner = nil
            return true
        } catch {
            codeError = error.localizedDescription
            return false
        }
    }

    func unfollow() async {
        await sp.follow.unfollow()
        banner = nil
    }

    func loadFollowers() async {
        loadingFollowers = true
        followers = await sp.follow.listFollowers() ?? followers
        loadingFollowers = false
    }

    func remove(_ follower: Follower) async {
        guard confirmRemove == follower.uid else {
            confirmRemove = follower.uid
            return
        }
        confirmRemove = nil
        do {
            try await sp.follow.removeFollower(follower.uid)
            followers.removeAll { $0.uid == follower.uid }
            followerCount = followers.count
        } catch {
            banner = Banner(severity: .warning, title: "", message: error.localizedDescription)
        }
    }
}

/// The first-run tour. Nothing is saved until the end, so skipping keeps every default.
@MainActor
@Observable
final class OnboardingModel {
    weak var app: AppModel?

    var step = 0
    var forward = true
    var picked: Set<String> = []
    var intervalMinutes = 0
    var dailyTime = "09:00"
    var matchTimeOfDay = true
    var startAtLogin = true
    var photoPerMonitor = false
    var displays = 1
    var finishedThisRun = false

    static let intervals: [(label: String, minutes: Int, description: String)] = [
        ("Once a day", 0, "A new photo every day, at the time you pick."),
        ("Every hour", 60, "Something new all day long."),
        ("Every 3 hours", 180, "Morning, afternoon and evening."),
        ("Every 6 hours", 360, "A few times a day."),
        ("Every 12 hours", 720, "Day and night."),
    ]

    func start(from s: OrielSettings, displays: Int) {
        step = 0
        picked = Set(s.topics.filter { !$0.isCustom && $0.enabled }.map(\.query))
        intervalMinutes = Self.intervals.contains { $0.minutes == s.intervalMinutes } ? s.intervalMinutes : 0
        dailyTime = s.dailyTime
        matchTimeOfDay = s.matchTimeOfDay
        startAtLogin = s.startAtLogin
        photoPerMonitor = s.photoPerMonitor
        self.displays = displays
    }

    func go(_ to: Int) {
        forward = to > step
        step = to
    }

    var hint: String {
        switch picked.count {
        case 0: "Pick at least one to continue."
        case 1: "1 picked. Pick a few more for variety."
        default: "\(picked.count) picked. Nice mix."
        }
    }

    var interestsSummary: String {
        let names = OrielSettings.builtInTopics.filter(picked.contains).map(\.capitalizedFirst)
        if names.count <= 3 { return names.joined(separator: ", ") }
        return "\(names.prefix(3).joined(separator: ", ")) and \(names.count - 3) more"
    }

    var scheduleSummary: String {
        intervalMinutes == 0 ? "Every day at \(dailyTime)" : Self.intervals.first { $0.minutes == intervalMinutes }!.label
    }

    var appliedSummary: String {
        (photoPerMonitor && displays > 1 ? "Each display its own photo" : "All displays") + (matchTimeOfDay ? ", matching the time of day" : "")
    }

    func save(into s: inout OrielSettings) {
        for i in s.topics.indices where !s.topics[i].isCustom { s.topics[i].enabled = picked.contains(s.topics[i].query) }
        s.intervalMinutes = intervalMinutes
        s.dailyTime = dailyTime
        s.matchTimeOfDay = matchTimeOfDay
        s.startAtLogin = startAtLogin
        s.photoPerMonitor = photoPerMonitor
        s.onboardingDone = true
    }
}

extension String {
    /// "northern lights" shown as "Northern lights".
    var capitalizedFirst: String { prefix(1).uppercased() + dropFirst() }
}
