import Foundation

/// Where Oriel keeps its files. Overridable so tests can use a temp folder.
public struct AppPaths: Sendable {
    /// `~/Library/Application Support/Oriel`: settings, state, history database, logs.
    public let dataRoot: URL
    /// Default wallpaper folder (`~/Pictures/Oriel`); the user can pick another one.
    public let picturesRoot: URL

    public init(dataRoot: URL? = nil, picturesRoot: URL? = nil) {
        let fm = FileManager.default
        self.dataRoot = dataRoot
            ?? fm.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appending(path: "Oriel", directoryHint: .isDirectory)
        self.picturesRoot = picturesRoot
            ?? fm.urls(for: .picturesDirectory, in: .userDomainMask)[0].appending(path: "Oriel", directoryHint: .isDirectory)
    }

    public var settingsFile: URL { dataRoot.appending(path: "settings.json") }
    public var stateFile: URL { dataRoot.appending(path: "state.json") }
    /// The next wallpaper, downloaded ahead of time so the change is instant and works offline.
    public var prefetchDir: URL { dataRoot.appending(path: "next", directoryHint: .isDirectory) }
    public var databaseFile: URL { dataRoot.appending(path: "oriel.db") }
    public var logsDir: URL { dataRoot.appending(path: "logs", directoryHint: .isDirectory) }

    public func ensureCreated() throws {
        let fm = FileManager.default
        try fm.createDirectory(at: dataRoot, withIntermediateDirectories: true)
        try fm.createDirectory(at: logsDir, withIntermediateDirectories: true)
    }
}
