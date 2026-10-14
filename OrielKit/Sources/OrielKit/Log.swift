import Foundation
import os
import Synchronization

/// One category of Oriel's log (named after the type that writes, like Windows: "WallpaperService").
/// Lines go to a daily file, `~/Library/Logs/Oriel/oriel-YYYY-MM-DD.log`, and to the system log. Never log secrets.
public struct OrielLogger: Sendable {
    let category: String

    public init(_ category: String) {
        self.category = category
    }

    public func debug(_ message: String) { LogFile.shared.write(.debug, category, message, nil) }
    public func info(_ message: String) { LogFile.shared.write(.info, category, message, nil) }
    public func warning(_ message: String, _ error: Error? = nil) { LogFile.shared.write(.warning, category, message, error) }
    public func error(_ message: String, _ error: Error? = nil) { LogFile.shared.write(.error, category, message, error) }
}

public enum Log {
    public static let subsystem = "uz.akbarjon.oriel"

    public static let app = OrielLogger("App")
    public static let settings = OrielLogger("JsonSettingsStore")
    public static let history = OrielLogger("SqliteHistoryStore")
    public static let provider = OrielLogger("UnsplashProvider")
    public static let wallpaper = OrielLogger("WallpaperService")
    public static let retention = OrielLogger("RetentionService")
    public static let mover = OrielLogger("WallpaperFolderMover")
    public static let scheduler = OrielLogger("DailyScheduler")
    public static let auth = OrielLogger("AuthService")
    public static let sync = OrielLogger("SyncPublisher")
    public static let settingsSync = OrielLogger("SettingsSync")
    public static let favorites = OrielLogger("FavoritesSync")
    public static let slots = OrielLogger("SlotClaimer")
    public static let pulse = OrielLogger("PulseListener")
    public static let follow = OrielLogger("FollowService")
    public static let share = OrielLogger("ShareService")
    public static let today = OrielLogger("TodayService")
    public static let stats = OrielLogger("StatsReporter")
    public static let launchd = OrielLogger("LaunchAgents")
    public static let cli = OrielLogger("Cli")

    /// Where the daily log files go. Set once at startup (tests leave it unset: nothing is written).
    public static func configure(folder: URL, minimum: LogFile.Level = .info) {
        LogFile.shared.configure(folder: folder, minimum: minimum)
    }
}

/// The daily log file. Writing never throws: logging must never crash the app.
public final class LogFile: Sendable {
    public enum Level: Int, Sendable, Comparable {
        case debug, info, warning, error

        var tag: String {
            switch self {
            case .debug: "DEBU"
            case .info: "INFO"
            case .warning: "WARN"
            case .error: "ERRO"
            }
        }

        var osType: OSLogType {
            switch self {
            case .debug: .debug
            case .info: .info
            case .warning: .default
            case .error: .error
            }
        }

        public static func < (a: Level, b: Level) -> Bool { a.rawValue < b.rawValue }
    }

    static let shared = LogFile()

    private struct Config {
        var folder: URL?
        var minimum: Level = .info
    }

    private let config = Mutex(Config())
    private let system = os.Logger(subsystem: Log.subsystem, category: "Oriel")

    func configure(folder: URL, minimum: Level) {
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        config.withLock { $0 = Config(folder: folder, minimum: minimum) }
    }

    func write(_ level: Level, _ category: String, _ message: String, _ error: Error?) {
        var text = message
        if let error { text += " | \(type(of: error)): \(error.localizedDescription)" }
        system.log(level: level.osType, "\(category, privacy: .public): \(text, privacy: .public)")

        config.withLock { c in
            guard let folder = c.folder, level >= c.minimum else { return }
            let now = Date()
            let day = Self.dayFormat.string(from: now)
            let line = "\(Self.timeFormat.string(from: now)) [\(level.tag)] [\(ProcessInfo.processInfo.processIdentifier)] \(category): \(text)\n"
            let file = folder.appending(path: "oriel-\(day).log")
            guard let data = line.data(using: .utf8) else { return }
            if let handle = try? FileHandle(forWritingTo: file) {
                _ = try? handle.seekToEnd()
                try? handle.write(contentsOf: data)
                try? handle.close()
            } else {
                try? data.write(to: file)
            }
        }
    }

    private static let dayFormat: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    private static let timeFormat: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "HH:mm:ss.SSS"
        return f
    }()
}
