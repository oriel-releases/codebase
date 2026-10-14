import AppKit
import Observation
import OrielKit
import ServiceManagement

/// Everything the windows and the menu bar show, and the actions they take. One per app, on the main thread.
@MainActor
@Observable
final class AppModel {
    let paths: AppPaths
    let store: JSONSettingsStore
    let history: HistoryStore
    let setter: MacWallpaperSetter
    let provider: UnsplashProvider
    let service: WallpaperService
    let retention: RetentionService

    /// The user's choices. Every edit is saved at once.
    var settings: OrielSettings {
        didSet {
            guard settings != oldValue else { return }
            store.save(settings)
            settingsChanged(from: oldValue)
        }
    }
    private(set) var state: OrielState
    private(set) var entries: [HistoryEntry] = []
    private(set) var isChanging = false
    /// What the last "Change now" or history action said, shown on Home until the next one.
    var notice: String?

    /// Opens (or brings forward) the main window. Set by the app delegate.
    var showWindow: () -> Void = {}

    private var schedulerTask: Task<Void, Never>?
    private var observers: [NSObjectProtocol] = []

    init() {
        paths = AppPaths()
        try? paths.ensureCreated()
        store = JSONSettingsStore(paths: paths)
        do {
            history = try SQLiteHistoryStore(paths: paths)
        } catch {
            Log.history.fault("Couldn't open the history database: \(String(describing: error), privacy: .public)")
            // Keep running with an in-memory history rather than not starting at all.
            history = try! SQLiteHistoryStore(file: FileManager.default.temporaryDirectory.appending(path: "oriel-\(UUID().uuidString).db"))
        }
        setter = MacWallpaperSetter(paths: paths)
        provider = UnsplashProvider()
        service = WallpaperService(provider: provider, setter: setter, history: history, settings: store, paths: paths,
                                   isDarkMode: { UserDefaults.standard.string(forKey: "AppleInterfaceStyle") == "Dark" })
        retention = RetentionService(history: history, settings: store, setter: setter)
        settings = store.load()
        state = store.loadState()
        reload()
    }

    var current: HistoryEntry? { entries.first }
    var favorites: [HistoryEntry] { entries.filter(\.isFavorite) }
    var hasKey: Bool { provider.isConfigured }

    // MARK: - Lifecycle

    func start() {
        if let current, FileManager.default.fileExists(atPath: current.filePath) {
            setter.adopt(current.fileURL, fit: settings.fit)
        }
        let center = NSWorkspace.shared.notificationCenter
        observers = [
            // macOS only changes the current Space: give the others the photo as you visit them.
            center.addObserver(forName: NSWorkspace.activeSpaceDidChangeNotification, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.reapplyOnSpaces() }
            },
            // After sleep the clock jumped: check right away instead of finishing the old wait.
            center.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.runScheduler(startup: false) }
            },
        ]
        syncLoginItem()
        runScheduler(startup: true)
        Task.detached(priority: .background) { [retention] in retention.cleanup() }
    }

    func reload() {
        entries = history.list()
        state = store.loadState()
    }

    // MARK: - Actions

    func changeNow() {
        guard !isChanging else { return }
        Task { await change(force: true) }
    }

    func setPaused(_ paused: Bool) {
        settings.paused = paused
    }

    func reapply(_ entry: HistoryEntry) {
        Task {
            do {
                _ = try await service.reapply(entry)
                notice = nil
            } catch {
                notice = error.localizedDescription
            }
            reload()
        }
    }

    func toggleFavorite(_ entry: HistoryEntry) {
        history.setFavorite(entry.id, !entry.isFavorite)
        reload()
    }

    func delete(_ entry: HistoryEntry) {
        retention.delete(entry.id)
        reload()
    }

    func showInFinder(_ entry: HistoryEntry) {
        NSWorkspace.shared.activateFileViewerSelecting([entry.fileURL])
    }

    func open(_ link: String) {
        if let url = URL(string: link) { NSWorkspace.shared.open(url) }
    }

    var wallpaperFolder: URL { paths.wallpaperFolder(settings) }

    /// Moves every photo to a new, empty folder and uses it from now on.
    func chooseWallpaperFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.prompt = "Use This Folder"
        panel.message = "Pick an empty folder. Oriel moves your photos into it."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try WallpaperFolderMover(settings: store, history: history, setter: setter, paths: paths).move(to: url)
            settings = store.load()
            notice = nil
        } catch {
            notice = error.localizedDescription
        }
        reload()
    }

    /// When "Change now" works again, or nil when it does now.
    var changeNowAvailableAt: Date? { WallpaperService.manualChangeAvailableAt(state, now: Date()) }

    // MARK: - Schedule

    private func change(force: Bool) async {
        isChanging = true
        defer { isChanging = false }
        let result = await service.apply(force: force)
        switch result.status {
        case .applied:
            notice = nil
            if let file = result.entries.first?.fileURL { setter.adopt(file, fit: settings.fit) }
        case .failed, .rateLimited, .busy:
            if force { notice = result.error }
        default:
            break
        }
        reload()
    }

    /// Sleeps until the next change is due (at most 10 minutes, which also covers clock changes), changes, repeats.
    private func runScheduler(startup: Bool) {
        schedulerTask?.cancel()
        schedulerTask = Task { [weak self] in
            var startupCheck = startup
            while !Task.isCancelled {
                guard let self else { return }
                let s = self.store.load(), st = self.store.loadState()
                if ScheduleRules.isDue(s, st, Date(), isStartupCheck: startupCheck) {
                    await self.change(force: false)
                }
                startupCheck = false
                let wait = ScheduleRules.nextCheckIn(self.store.load(), self.store.loadState(), Date())
                try? await Task.sleep(for: .seconds(wait))
            }
        }
    }

    private func settingsChanged(from old: OrielSettings) {
        let scheduleEdited = old.dailyTime != settings.dailyTime || old.intervalMinutes != settings.intervalMinutes
            || old.timeZone != settings.timeZone
        if scheduleEdited {
            // A new schedule starts from now: editing it never changes the wallpaper by itself.
            store.updateState { $0.scheduleStartedAt = Date() }
            state = store.loadState()
        }
        if scheduleEdited || old.paused != settings.paused { runScheduler(startup: false) }
        if old.openAtLogin != settings.openAtLogin { syncLoginItem() }
        if old.fit != settings.fit, let current, FileManager.default.fileExists(atPath: current.filePath) {
            reapply(current)
        }
    }

    private func reapplyOnSpaces() {
        let folders = [wallpaperFolder, paths.dataRoot].map { $0.standardizedFileURL.path(percentEncoded: false) }
        setter.reapplyIfNeeded { url in
            let path = url.standardizedFileURL.path(percentEncoded: false)
            return folders.contains { path.hasPrefix($0) }
        }
    }

    // MARK: - Login item

    /// Registers Oriel as a login item when "Open at login" is on. Only for an installed copy, so a development
    /// build never ends up starting at login.
    private func syncLoginItem() {
        let service = SMAppService.mainApp
        do {
            if settings.openAtLogin {
                guard Bundle.main.bundlePath.hasPrefix("/Applications/") else { return }
                if service.status != .enabled { try service.register() }
            } else if service.status == .enabled {
                try service.unregister()
            }
        } catch {
            Log.settings.warning("Login item: \(error.localizedDescription, privacy: .public)")
        }
    }

    var appVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "?"
    }
}
