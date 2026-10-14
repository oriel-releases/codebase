import Foundation

/// Where Oriel keeps its files. Overridable so tests can use a temp folder.
public struct AppPaths: Sendable {
    /// `~/Library/Application Support/Oriel`: settings, state, account, history database.
    public let dataRoot: URL
    /// Default wallpaper folder (`~/Pictures/Oriel`); the user can pick another one.
    public let picturesRoot: URL
    /// `~/Library/Logs/Oriel`, where Console.app finds the daily logs.
    public let logsDir: URL

    public init(dataRoot: URL? = nil, picturesRoot: URL? = nil, logsDir: URL? = nil) {
        let fm = FileManager.default
        let data = dataRoot
            ?? fm.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appending(path: "Oriel", directoryHint: .isDirectory)
        self.dataRoot = data
        self.picturesRoot = picturesRoot
            ?? fm.urls(for: .picturesDirectory, in: .userDomainMask)[0].appending(path: "Oriel", directoryHint: .isDirectory)
        self.logsDir = logsDir ?? (dataRoot == nil
            ? fm.urls(for: .libraryDirectory, in: .userDomainMask)[0].appending(path: "Logs/Oriel", directoryHint: .isDirectory)
            : data.appending(path: "logs", directoryHint: .isDirectory))
    }

    public var settingsFile: URL { dataRoot.appending(path: "settings.json") }
    public var stateFile: URL { dataRoot.appending(path: "state.json") }
    /// The signed-in account and its refresh token (owner-only permissions).
    public var accountFile: URL { dataRoot.appending(path: "auth.json") }
    /// What the anonymous daily counts already counted on this device (see StatsReporter).
    public var statsFile: URL { dataRoot.appending(path: "stats.json") }
    /// The Wallpaper of the day list, kept for a few days.
    public var todayFile: URL { dataRoot.appending(path: "today.json") }
    /// The next wallpapers, downloaded ahead of time so a change is instant and works offline.
    public var prefetchDir: URL { dataRoot.appending(path: "next", directoryHint: .isDirectory) }
    public var databaseFile: URL { dataRoot.appending(path: "oriel.db") }
    /// Held while a wallpaper change or folder move is under way (see ApplyLock).
    public var lockFile: URL { dataRoot.appending(path: "apply.lock") }

    /// Where photos are saved: the folder the user picked, else the default.
    public func wallpaperFolder(_ settings: OrielSettings) -> URL {
        guard let custom = settings.wallpaperFolder, !custom.trimmingCharacters(in: .whitespaces).isEmpty else {
            return picturesRoot
        }
        return URL(filePath: custom, directoryHint: .isDirectory)
    }

    public func ensureCreated() throws {
        let fm = FileManager.default
        try fm.createDirectory(at: dataRoot, withIntermediateDirectories: true)
        try fm.createDirectory(at: logsDir, withIntermediateDirectories: true)
    }
}

/// Web addresses the app points people to.
public enum OrielLinks {
    public static let site = "https://oriel.akbarjon.uz"
    /// Signing in happens on the website, which hands the app an oriel://auth link.
    public static let login = "https://oriel.akbarjon.uz/login"
    public static let loginEmail = "https://oriel.akbarjon.uz/login?method=email"
    /// The account page: profile, current wallpaper and where you're signed in.
    public static let account = "https://oriel.akbarjon.uz/me"
    /// A shared wallpaper's page (`/share/{id}`): one click there opens Oriel and sets the photo.
    public static let share = "https://oriel.akbarjon.uz/share"
    /// Someone's follow page (`/follow/{code}`): one click there opens Oriel and follows.
    public static let follow = "https://oriel.akbarjon.uz/follow"
    /// The Wallpaper of the day list the website publishes for the apps.
    public static let todayList = "https://oriel.akbarjon.uz/today.json"
    /// The Telegram support bot: questions, bugs and ideas.
    public static let support = "https://t.me/oriel_support_bot"
    public static let releases = "https://github.com/oriel-releases/macos"
}
