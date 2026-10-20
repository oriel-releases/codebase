import AppKit
import OrielKit
import ServiceManagement

/// Every service, made once. The composition root shared by the app and the command-line runs.
struct Services: Sendable {
    let paths: AppPaths
    let store: JSONSettingsStore
    let history: HistoryStore
    let setter: MacWallpaperSetter
    let provider: UnsplashProvider
    let ai: PollinationsClient
    let firebase: FirebaseClient
    let auth: AuthService
    let syncPublisher: SyncPublisher
    let wallpapers: WallpaperService
    let retention: RetentionService
    let follow: FollowService
    let share: ShareService
    let settingsSync: SettingsSync
    let favoritesSync: FavoritesSync
    let pulse: PulseListener
    let today: TodayService
    let stats: StatsReporter
    let scheduler: DailyScheduler
    let agents: LaunchAgents
    let scheduleSync: ScheduleSync
}

enum AppHost {
    static var appVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.0.0"
    }

    /// The executable launchd runs (`Oriel.app/Contents/MacOS/Oriel`).
    static var executable: String { Bundle.main.executablePath ?? CommandLine.arguments[0] }

    static func build() -> Services {
        let paths = AppPaths()
        try? paths.ensureCreated()
        Log.configure(folder: paths.logsDir)

        let store = JSONSettingsStore(paths: paths)
        let history: HistoryStore
        do {
            history = try SQLiteHistoryStore(paths: paths)
        } catch {
            Log.app.error("Couldn't open the history database", error)
            // Keep running with a throwaway history rather than not starting at all.
            history = try! SQLiteHistoryStore(file: FileManager.default.temporaryDirectory.appending(path: "oriel-\(UUID().uuidString).db"))
        }
        let setter = MacWallpaperSetter(paths: paths)
        let provider = UnsplashProvider()
        let firebase = FirebaseClient()
        let auth = AuthService(store: FileAccountStore(paths: paths), firebase: firebase, appVersion: appVersion,
                               deviceName: Host.current().localizedName ?? "Mac")
        let ai = PollinationsClient(paths: paths)
        let sampler = AccentSampler()
        let publisher = SyncPublisher(auth: auth, firebase: firebase, history: history, settings: store, accent: sampler)
        let wallpapers = WallpaperService(
            provider: provider, setter: setter, history: history, settings: store, paths: paths, auth: auth, sync: publisher,
            slots: SlotClaimer(auth: auth, firebase: firebase), ai: ai, accentSampler: sampler,
            isDarkMode: { UserDefaults.standard.string(forKey: "AppleInterfaceStyle") == "Dark" })
        let retention = RetentionService(history: history, settings: store, setter: setter)
        let follow = FollowService(auth: auth, firebase: firebase, store: store, wallpapers: wallpapers, history: history)
        let settingsSync = SettingsSync(auth: auth, firebase: firebase, store: store)
        let favoritesSync = FavoritesSync(auth: auth, firebase: firebase, history: history, store: store)
        let pulse = PulseListener(auth: auth, follow: follow, settings: settingsSync, favorites: favoritesSync)
        let stats = StatsReporter(auth: auth, firebase: firebase, history: history, paths: paths)
        let agents = LaunchAgents()
        return Services(
            paths: paths, store: store, history: history, setter: setter, provider: provider, ai: ai, firebase: firebase, auth: auth,
            syncPublisher: publisher, wallpapers: wallpapers, retention: retention, follow: follow,
            share: ShareService(auth: auth, firebase: firebase), settingsSync: settingsSync, favoritesSync: favoritesSync,
            pulse: pulse, today: TodayService(paths: paths), stats: stats,
            scheduler: DailyScheduler(wallpapers: wallpapers, retention: retention, store: store, auth: auth, follow: follow,
                                      stats: stats, pulse: pulse),
            agents: agents,
            scheduleSync: ScheduleSync(store: store, startup: LoginItem(), agents: agents))
    }
}

/// "Open at login" with SMAppService. Only for an installed copy, so a development build never starts at login.
struct LoginItem: StartupRegistration {
    func set(_ enabled: Bool) {
        let service = SMAppService.mainApp
        do {
            if enabled {
                guard Self.isInstalled else { return }
                if service.status != .enabled { try service.register() }
            } else if service.status == .enabled {
                try service.unregister()
            }
        } catch {
            Log.app.warning("Open at login: \(error.localizedDescription)")
        }
    }

    static var isInstalled: Bool {
        let path = Bundle.main.bundlePath
        return path.hasPrefix("/Applications/") || path.hasPrefix(NSHomeDirectory() + "/Applications/")
    }
}
