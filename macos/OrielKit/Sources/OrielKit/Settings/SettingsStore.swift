import Foundation
import Synchronization

public protocol SettingsStore: Sendable {
    func load() -> OrielSettings
    func save(_ settings: OrielSettings)
    func loadState() -> OrielState
    func saveState(_ state: OrielState)
    /// Load-modify-save in one step, so different parts of the app never overwrite each other's changes.
    @discardableResult func update(_ change: (inout OrielSettings) -> Void) -> OrielSettings
    @discardableResult func updateState(_ change: (inout OrielState) -> Void) -> OrielState
}

public extension Notification.Name {
    /// Posted (on the saving thread) after settings.json was saved.
    static let orielSettingsChanged = Notification.Name("OrielSettingsChanged")
}

/// settings.json and state.json in the data folder. Writes go to a temp file first, so a crash never leaves half a file.
public final class JSONSettingsStore: SettingsStore {
    private let paths: AppPaths
    private let gate = Mutex(())

    public init(paths: AppPaths) {
        self.paths = paths
    }

    @discardableResult
    public func update(_ change: (inout OrielSettings) -> Void) -> OrielSettings {
        let saved = gate.withLock { _ in
            var s: OrielSettings = readUnlocked(paths.settingsFile)
            s.updateBuiltInTopics()
            change(&s)
            writeUnlocked(s, to: paths.settingsFile)
            return s
        }
        NotificationCenter.default.post(name: .orielSettingsChanged, object: self)
        return saved
    }

    /// Atomic: the state is shared by the scheduler, the syncs and the UI, which save it at the same moments.
    @discardableResult
    public func updateState(_ change: (inout OrielState) -> Void) -> OrielState {
        gate.withLock { _ in
            var s: OrielState = readUnlocked(paths.stateFile)
            change(&s)
            writeUnlocked(s, to: paths.stateFile)
            return s
        }
    }

    public func load() -> OrielSettings {
        var settings: OrielSettings = read(paths.settingsFile)
        settings.updateBuiltInTopics()
        return settings
    }

    public func save(_ settings: OrielSettings) {
        write(settings, to: paths.settingsFile)
        NotificationCenter.default.post(name: .orielSettingsChanged, object: self)
    }

    public func loadState() -> OrielState { read(paths.stateFile) }

    public func saveState(_ state: OrielState) { write(state, to: paths.stateFile) }

    private func read<T: Codable & Defaultable & Sendable>(_ file: URL) -> T {
        gate.withLock { _ in readUnlocked(file) }
    }

    private func write<T: Encodable>(_ value: T, to file: URL) {
        gate.withLock { _ in writeUnlocked(value, to: file) }
    }

    private func readUnlocked<T: Codable & Defaultable>(_ file: URL) -> T {
        guard let data = try? Data(contentsOf: file) else { return T() }
        // A corrupt file shouldn't brick the app; fall back to defaults.
        return (try? Self.decoder.decode(T.self, from: data)) ?? T()
    }

    private func writeUnlocked<T: Encodable>(_ value: T, to file: URL) {
        do {
            try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            try Self.encoder.encode(value).write(to: file, options: .atomic)
        } catch {
            Log.settings.error("Couldn't save \(file.lastPathComponent): \(error.localizedDescription)")
        }
    }

    static let encoder: JSONEncoder = {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        // With milliseconds: settings sync compares these with Firestore's timestamps.
        e.dateEncodingStrategy = .custom { date, encoder in
            var c = encoder.singleValueContainer()
            try c.encode(ISO8601.format(date))
        }
        return e
    }()

    static let decoder: JSONDecoder = {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .custom { decoder in
            let text = try decoder.singleValueContainer().decode(String.self)
            guard let date = ISO8601.parse(text) else {
                throw DecodingError.dataCorrupted(.init(codingPath: decoder.codingPath, debugDescription: "Not a date: \(text)"))
            }
            return date
        }
        return d
    }()
}

/// Types with a sensible "nothing saved yet" value.
protocol Defaultable {
    init()
}

extension OrielSettings: Defaultable {}
extension OrielState: Defaultable {}

/// ISO 8601 in UTC with milliseconds ("2026-10-14T09:00:00.123Z"), reading it with or without the fraction.
public enum ISO8601 {
    /// Rounded to the millisecond, not cut: a time parsed from ".539" is 0.53899999… as a number, and cutting it would
    /// write ".538" (then "changed since" asks for the same change again and again).
    public static func format(_ date: Date) -> String {
        let ms = Int64((date.timeIntervalSince1970 * 1000).rounded())
        let seconds = Date(timeIntervalSince1970: Double(ms.quotient(1000)))
        let base = Date.ISO8601FormatStyle().format(seconds) // "2026-10-15T18:45:21Z"
        return base.dropLast() + String(format: ".%03lldZ", ms.remainder(1000))
    }

    /// Keeps every fractional digit: Firestore writes microseconds, and "changed since" queries compare exactly.
    public static func parse(_ text: String) -> Date? {
        if let dot = text.firstIndex(of: "."),
           let end = text[text.index(after: dot)...].firstIndex(where: { !$0.isNumber }) {
            let digits = text[text.index(after: dot)..<end]
            if let base = try? Date.ISO8601FormatStyle().parse(String(text[..<dot] + text[end...])), let fraction = Double("0." + digits) {
                return base.addingTimeInterval(fraction)
            }
        }
        return try? Date.ISO8601FormatStyle().parse(text)
    }
}

extension Int64 {
    /// Floor division and its remainder, also for times before 1970.
    func quotient(_ d: Int64) -> Int64 { (self - remainder(d)) / d }
    func remainder(_ d: Int64) -> Int64 { ((self % d) + d) % d }
}
