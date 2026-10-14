import Foundation
import Testing
@testable import OrielKit

struct WallpaperFolderMoverTests {
    let dir = TempDir()
    let setter = FakeSetter()
    let store: JSONSettingsStore
    let history: SQLiteHistoryStore
    let mover: WallpaperFolderMover
    let newFolder: URL

    init() throws {
        let paths = dir.paths
        try paths.ensureCreated()
        store = JSONSettingsStore(paths: paths)
        history = try SQLiteHistoryStore(paths: paths)
        mover = WallpaperFolderMover(settings: store, history: history, setter: setter, paths: paths)
        newFolder = paths.dataRoot.appending(path: "new place", directoryHint: .isDirectory)
    }

    func addPhoto(_ name: String) throws -> Int64 {
        try history.add(Fixtures.entry(name, at: Date(), file: Fixtures.file(name, in: dir.paths.picturesRoot)))
    }

    func exists(_ path: String) -> Bool { FileManager.default.fileExists(atPath: path) }

    @Test func movesPhotosAndUpdatesHistorySettingsAndTheDesktop() throws {
        let a = try addPhoto("a.jpg")
        let b = try addPhoto("b.jpg")
        try setter.setForAll(history.get(b)!.fileURL, fit: .fill)

        #expect(try mover.move(to: newFolder) == 2)

        let newPath = newFolder.path(percentEncoded: false)
        #expect(history.get(a)?.filePath == newFolder.appending(path: "a.jpg").path(percentEncoded: false))
        #expect(exists(history.get(b)!.filePath))
        #expect(store.load().wallpaperFolder.map { $0.hasPrefix(newPath.dropLast()) } == true)
        #expect(setter.current(on: nil) == newFolder.appending(path: "b.jpg"))
        #expect(!exists(dir.paths.picturesRoot.path(percentEncoded: false))) // the empty old folder is cleaned up
    }

    @Test func refusesAFolderThatIsNotEmpty() throws {
        let a = try addPhoto("a.jpg")
        Fixtures.file("mine.txt", in: newFolder)

        #expect(throws: WallpaperError.self) { try mover.move(to: newFolder) }

        #expect(exists(history.get(a)!.filePath))
        #expect(store.load().wallpaperFolder == nil)
    }

    @Test func aFailedMovePutsEverythingBack() throws {
        let a = try addPhoto("a.jpg")
        _ = try addPhoto("b.jpg")
        // An empty folder Oriel can't write into.
        try FileManager.default.createDirectory(at: newFolder, withIntermediateDirectories: true)
        try FileManager.default.setAttributes([.posixPermissions: 0o555], ofItemAtPath: newFolder.path(percentEncoded: false))
        defer { try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: newFolder.path(percentEncoded: false)) }

        #expect(throws: WallpaperError.self) { try mover.move(to: newFolder) }

        #expect(exists(history.get(a)!.filePath))
        #expect(store.load().wallpaperFolder == nil)
    }

    @Test func refusesWhileAChangeIsUnderWay() throws {
        _ = try addPhoto("a.jpg")
        let busy = ApplyLock.tryAcquire(dir.paths.lockFile)

        #expect(throws: WallpaperError.self) { try mover.move(to: newFolder) }
        withExtendedLifetime(busy) {}
    }
}
