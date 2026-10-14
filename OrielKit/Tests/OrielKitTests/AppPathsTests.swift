import Foundation
import Testing
@testable import OrielKit

struct AppPathsTests {
    @Test func defaultsLiveInApplicationSupportAndPictures() {
        let paths = AppPaths()
        #expect(paths.dataRoot.path(percentEncoded: false).hasSuffix("/Library/Application Support/Oriel/"))
        #expect(paths.picturesRoot.path(percentEncoded: false).hasSuffix("/Pictures/Oriel/"))
    }

    @Test func filesSitUnderDataRoot() throws {
        let root = FileManager.default.temporaryDirectory.appending(path: UUID().uuidString, directoryHint: .isDirectory)
        defer { try? FileManager.default.removeItem(at: root) }
        let paths = AppPaths(dataRoot: root, picturesRoot: root.appending(path: "pics"))

        try paths.ensureCreated()

        #expect(paths.settingsFile == root.appending(path: "settings.json"))
        #expect(paths.databaseFile == root.appending(path: "oriel.db"))
        #expect(FileManager.default.fileExists(atPath: paths.logsDir.path(percentEncoded: false)))
    }

    @Test func missingOrBlankKeyIsNoKey() {
        #expect(BuiltInApiKeyStore(bundle: Bundle(for: Marker.self)).hasKey == false)
    }
}

private final class Marker {}
