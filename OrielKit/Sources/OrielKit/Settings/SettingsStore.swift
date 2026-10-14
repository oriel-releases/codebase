import Foundation
import Synchronization

public protocol SettingsStore: Sendable {
    func load() -> OrielSettings
    func save(_ settings: OrielSettings)
    func loadState() -> OrielState
    func saveState(_ state: OrielState)
}

public extension SettingsStore {
    /// Load-modify-save, so different screens never overwrite each other's changes.
    @discardableResult
    func update(_ change: (inout OrielSettings) -> Void) -> OrielSettings {
        var s = load()
        change(&s)
        save(s)
        return s
    }

    @discardableResult
    func updateState(_ change: (inout OrielState) -> Void) -> OrielState {
        var s = loadState()
        change(&s)
        saveState(s)
        return s
    }
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
        gate.withLock { _ in
            guard let data = try? Data(contentsOf: file) else { return T() }
            // A corrupt file shouldn't brick the app; fall back to defaults.
            return (try? Self.decoder.decode(T.self, from: data)) ?? T()
        }
    }

    private func write<T: Encodable>(_ value: T, to file: URL) {
        gate.withLock { _ in
            do {
                try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
                try Self.encoder.encode(value).write(to: file, options: .atomic)
            } catch {
                Log.settings.error("Couldn't save \(file.lastPathComponent, privacy: .public): \(error.localizedDescription, privacy: .public)")
            }
        }
    }

    static let encoder: JSONEncoder = {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        e.dateEncodingStrategy = .iso8601
        return e
    }()

    static let decoder: JSONDecoder = {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        return d
    }()
}

/// Types with a sensible "nothing saved yet" value.
protocol Defaultable {
    init()
}

extension OrielSettings: Defaultable {}
extension OrielState: Defaultable {}
