import AppKit
import Observation
import OrielKit

/// A banner (Windows' InfoBar): a result or a problem, with a severity.
struct Banner: Equatable, Identifiable {
    enum Severity { case info, success, warning, error }

    var severity: Severity
    var title: String
    var message: String
    var closable = true

    var id: String { "\(severity)|\(title)|\(message)" }
}

enum Page: String, CaseIterable, Identifiable {
    case home, search, history, favorites, friends, settings

    var id: Self { self }

    var title: String {
        switch self {
        case .home: "Home"
        case .search: "Search"
        case .history: "History"
        case .favorites: "Favorites"
        case .friends: "Friends"
        case .settings: "Settings"
        }
    }

    var icon: String {
        switch self {
        case .home: "house"
        case .search: "magnifyingglass"
        case .history: "clock.arrow.circlepath"
        case .favorites: "star"
        case .friends: "person.2"
        case .settings: "gearshape"
        }
    }
}

/// What the window and the menu bar show, and the actions they take. One per app, on the main thread.
@MainActor
@Observable
final class AppModel {
    let sp: Services

    // Account
    private(set) var isSignedIn: Bool
    private(set) var isSigningOut = false
    private(set) var account: Account?

    // Settings, state, history
    /// The user's choices. Every edit is saved at once (and shared with the other devices by SettingsSync).
    var settings: OrielSettings {
        didSet { if settings != oldValue { sp.store.save(settings) } }
    }
    private(set) var state: OrielState
    private(set) var entries: [HistoryEntry] = []
    var current: HistoryEntry? { entries.first }

    // Navigation and overlays
    var page: Page = .home
    /// Search's sub-page.
    var showingInterests = false
    /// Where the keyboard should go next (set by the app delegate's key handling; the window applies it).
    var focusRequest: WindowFocus?
    var showOnboarding = false
    let shared: SharedPhotoModel
    let signIn: SignInModel
    let search: SearchModel
    let friends: FriendsModel
    let onboarding: OnboardingModel

    /// Why the shortcut couldn't be registered (shown in Settings), or nil.
    var hotkeyError: String?
    let updates = UpdateService(version: AppHost.appVersion)

    // Home
    private(set) var isBusy = false
    /// The words on Home's button while something is changing ("Finding a photo…").
    private(set) var phaseWords: String?
    var homeBanner: Banner?
    private(set) var limitMinutes: Int?
    private(set) var today: TodayPhoto?
    private var todayLoadedOn: LocalDate?
    var todayWords: String?
    var shareCopied = false

    /// Set by the app delegate.
    var showWindow: () -> Void = {}
    var isWindowVisible: () -> Bool = { false }

    private var limitTimer: Timer?

    init(services: Services) {
        sp = services
        isSignedIn = services.auth.isSignedIn
        account = services.auth.current
        settings = services.store.load()
        state = services.store.loadState()
        shared = SharedPhotoModel(services: services)
        signIn = SignInModel(services: services)
        search = SearchModel(services: services)
        friends = FriendsModel(services: services)
        onboarding = OnboardingModel()
        entries = services.history.list()
        shared.app = self
        signIn.app = self
        search.app = self
        friends.app = self
        onboarding.app = self
        subscribe()
        refreshLimit()
    }

    private func subscribe() {
        // Every core event is handled on the main thread.
        sp.wallpapers.phases.subscribe { [weak self] phase in Task { @MainActor in self?.phaseChanged(phase) } }
        sp.wallpapers.applied.subscribe { [weak self] _ in Task { @MainActor in self?.reload() } }
        sp.follow.changed.subscribe { [weak self] in
            Task { @MainActor in
                self?.reload()
                self?.friends.reload()
            }
        }
        sp.scheduler.attempted.subscribe { [weak self] result in Task { @MainActor in self?.showResult(result, manual: false) } }
        sp.auth.changed.subscribe { [weak self] in Task { @MainActor in self?.authChanged() } }
        sp.auth.signingOutChanged.subscribe { [weak self] on in Task { @MainActor in self?.isSigningOut = on } }
        NotificationCenter.default.addObserver(forName: .orielSettingsChanged, object: sp.store, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.settingsChangedOutside() }
        }
    }

    /// Settings saved by someone else (SettingsSync, FollowService): show them, and keep the schedule in step.
    private func settingsChangedOutside() {
        let fresh = sp.store.load()
        if fresh != settings { settings = fresh }
        state = sp.store.loadState()
        sp.scheduler.wake()
        let sync = sp.scheduleSync
        Task.detached(priority: .utility) { sync.sync(executable: AppHost.executable) }
        refreshLimit()
    }

    private func authChanged() {
        let was = isSignedIn
        isSignedIn = sp.auth.isSignedIn
        account = sp.auth.current
        sp.scheduler.wake()
        if isSignedIn {
            signIn.finished()
            // Back from the browser after signing in: bring Oriel to the front.
            if !was { showWindow() }
            evaluateOnboarding()
            Task { await sp.follow.followWaitingLink() }
            Task { await sp.syncPublisher.publishLatestIfMissing() }
        }
        reload()
    }

    // MARK: - Refresh

    func reload() {
        entries = sp.history.list()
        state = sp.store.loadState()
        refreshLimit()
        if !isBusy, let error = state.lastError {
            var message = error
            if let retry = state.nextRetryAt, retry > Date() {
                message += " Retrying automatically at \(Self.hhmm(retry))."
            }
            homeBanner = Banner(severity: .warning, title: "Last change didn't work", message: message)
        } else if let banner = homeBanner, banner.severity != .success, banner.title == "Last change didn't work" {
            homeBanner = nil
        }
    }

    private func refreshLimit() {
        let at = WallpaperService.manualChangeAvailableAt(sp.store.loadState(), now: Date())
        limitMinutes = at.map { WallpaperService.minutesUntil($0, now: Date()) }
        limitTimer?.invalidate()
        limitTimer = nil
        // While limited, look again every 15 s so the button comes back by itself.
        if limitMinutes != nil {
            limitTimer = Timer.scheduledTimer(withTimeInterval: 15, repeats: false) { [weak self] _ in
                MainActor.assumeIsolated { self?.refreshLimit() }
            }
        }
    }

    // MARK: - Phases and Change now

    private func phaseChanged(_ phase: ApplyPhase) {
        if phase == .done {
            isBusy = false
            phaseWords = nil
        } else {
            isBusy = true
            phaseWords = phase.text
        }
    }

    /// Home's (and the menu bar's) Change now.
    func changeNow() async {
        guard !isBusy else { return }
        isBusy = true
        phaseWords = ApplyPhase.finding.text
        homeBanner = nil
        let result = await sp.wallpapers.apply(force: true)
        showResult(result, manual: true)
        reload()
    }

    func showResult(_ r: ApplyResult, manual: Bool) {
        switch r.status {
        case .failed:
            let key = r.errorKind == .missingKey || r.errorKind == .unauthorized
            homeBanner = Banner(severity: key ? .error : .warning, title: "Couldn't change the wallpaper",
                                message: "\(r.error ?? "") Your current wallpaper was kept.")
        case .busy where manual:
            homeBanner = Banner(severity: .info, title: "Hang on", message: "Another change is already in progress.")
        case .rateLimited:
            homeBanner = Banner(severity: .info, title: "Slow down a little", message: r.error ?? "")
        case .signInRequired:
            showWindow()
        default:
            break
        }
        reload()
    }

    // MARK: - Actions

    func setPaused(_ paused: Bool) { settings.paused = paused }

    /// A double-click on the photo: only ever stars it.
    func like() {
        guard let current, !current.isFavorite else { return }
        sp.history.setFavorite(current.id, true)
        reload()
    }

    func toggleFavorite(_ entry: HistoryEntry) {
        sp.history.setFavorite(entry.id, !entry.isFavorite)
        reload()
    }

    func delete(_ entry: HistoryEntry) {
        sp.retention.delete(entry.id)
        reload()
    }

    func showInFinder(_ entry: HistoryEntry) {
        if !entry.filePath.isEmpty && FileManager.default.fileExists(atPath: entry.filePath) {
            NSWorkspace.shared.activateFileViewerSelecting([entry.fileURL])
        } else {
            openWallpaperFolder()
        }
    }

    func openWallpaperFolder() {
        let folder = sp.paths.wallpaperFolder(settings)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        NSWorkspace.shared.open(folder)
    }

    func open(_ link: String) {
        if let url = URL(string: link) { NSWorkspace.shared.open(url) }
    }

    /// Runs a change started from a button and reports its words to it, until it's fully done (sync included).
    func watchPhases(_ change: @escaping () async -> ApplyResult, report: @escaping @MainActor (String?) -> Void) async -> ApplyResult {
        let done = AsyncStream<Void>.makeStream()
        let token = sp.wallpapers.phases.subscribe { phase in
            if phase == .done { done.continuation.yield() }
            else { Task { @MainActor in report(phase.text) } }
        }
        let result = await change()
        if result.status == .applied {
            // Wait for "Done" (sharing with the other devices included), at most 20 s.
            await withTaskGroup(of: Void.self) { group in
                group.addTask { for await _ in done.stream { return } }
                group.addTask { try? await Task.sleep(for: .seconds(20)) }
                await group.next()
                group.cancelAll()
            }
        }
        sp.wallpapers.phases.unsubscribe(token)
        done.continuation.finish()
        report(nil)
        return result
    }

    // MARK: - Wallpaper of the day

    func loadToday() {
        let day = LocalDate(Date())
        guard todayLoadedOn != day else { return }
        todayLoadedOn = day
        Task { today = await sp.today.today() }
    }

    var todayIsCurrent: Bool { today != nil && current?.photoID == today?.id }

    func setToday() async {
        guard let today, todayWords == nil else { return }
        todayWords = "Finding it…"
        let result = await watchPhases({ [sp] in
            do {
                let photo = try await sp.wallpapers.sharedPhoto(today.id)
                return await sp.wallpapers.applyPhoto(photo, topic: "wallpaper of the day")
            } catch {
                return ApplyResult(.failed, error: error.localizedDescription)
            }
        }, report: { [weak self] words in self?.todayWords = words })
        if result.status == .failed {
            homeBanner = Banner(severity: .warning, title: "Couldn't set the wallpaper", message: result.error ?? "Try again.")
        }
        reload()
    }

    // MARK: - Onboarding

    /// The tour shows right after the first sign-in, when History is empty (or ORIEL_ONBOARDING=1).
    func evaluateOnboarding() {
        guard isSignedIn, !onboarding.finishedThisRun else {
            showOnboarding = false
            return
        }
        if ProcessInfo.processInfo.environment["ORIEL_ONBOARDING"] == "1" {
            onboarding.start(from: settings, displays: sp.setter.screens().count)
            showOnboarding = true
            return
        }
        if settings.onboardingDone {
            showOnboarding = false
            return
        }
        if entries.isEmpty {
            onboarding.start(from: settings, displays: sp.setter.screens().count)
            showOnboarding = true
        } else {
            // An existing user: never shown.
            settings.onboardingDone = true
            showOnboarding = false
        }
    }

    func finishOnboarding(changeNow: Bool) {
        onboarding.save(into: &settings)
        onboarding.finishedThisRun = true
        showOnboarding = false
        if changeNow && !shared.isOpen { Task { await self.changeNow() } }
    }

    func skipOnboarding() {
        settings.onboardingDone = true
        onboarding.finishedThisRun = true
        showOnboarding = false
    }

    // MARK: - Folder

    var folderBanner: Banner?
    private(set) var movingFolder = false

    func chooseWallpaperFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.directoryURL = FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask).first
        panel.prompt = "Use This Folder"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        movingFolder = true
        let mover = WallpaperFolderMover(settings: sp.store, history: sp.history, setter: sp.setter, paths: sp.paths)
        Task {
            let result = await Task.detached { () -> Result<Int, Error> in Result { try mover.move(to: url) } }.value
            movingFolder = false
            switch result {
            case .success(let n): folderBanner = Banner(severity: .success, title: "", message: n == 1 ? "Moved 1 photo." : "Moved \(n) photos.")
            case .failure(let e): folderBanner = Banner(severity: .error, title: "", message: e.localizedDescription)
            }
            settings = sp.store.load()
            reload()
        }
    }

    var wallpaperFolder: URL { sp.paths.wallpaperFolder(settings) }

    /// "09:05": times are shown in 24 hours, like Windows.
    static func hhmm(_ date: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "HH:mm"
        return f.string(from: date)
    }
}
