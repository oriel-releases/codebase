import os

/// Oriel's logs, in the system log. Read them with:
///   log show --last 1h --predicate 'subsystem == "uz.akbarjon.oriel"'
/// Never log secrets.
public enum Log {
    public static let subsystem = "uz.akbarjon.oriel"

    public static let settings = Logger(subsystem: subsystem, category: "settings")
    public static let history = Logger(subsystem: subsystem, category: "history")
    public static let provider = Logger(subsystem: subsystem, category: "provider")
    public static let wallpaper = Logger(subsystem: subsystem, category: "wallpaper")
}
