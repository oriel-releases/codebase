import Foundation

/// Switches the wallpaper folder and moves every photo there. All or nothing: if a file can't be moved, the ones
/// already moved go back and the old folder stays in use.
public struct WallpaperFolderMover: Sendable {
    private let settings: SettingsStore
    private let history: HistoryStore
    private let setter: WallpaperSetter
    private let paths: AppPaths

    public init(settings: SettingsStore, history: HistoryStore, setter: WallpaperSetter, paths: AppPaths) {
        self.settings = settings
        self.history = history
        self.setter = setter
        self.paths = paths
    }

    /// Returns how many photos were moved.
    @discardableResult
    public func move(to newFolder: URL) throws -> Int {
        let fm = FileManager.default
        let current = settings.load()
        let oldFolder = paths.wallpaperFolder(current).standardizedFileURL
        let newFolder = newFolder.standardizedFileURL
        guard Self.path(oldFolder) != Self.path(newFolder) else { return 0 }

        if let existing = try? fm.contentsOfDirectory(atPath: Self.path(newFolder)), !existing.isEmpty {
            throw WallpaperError("Pick an empty folder. Oriel moves your photos into it.")
        }

        // Don't race a wallpaper change that is saving into the old folder.
        guard let lock = ApplyLock.tryAcquire(paths.lockFile) else {
            throw WallpaperError("A wallpaper change is in progress. Try again in a moment.")
        }
        defer { withExtendedLifetime(lock) {} }

        try fm.createDirectory(at: newFolder, withIntermediateDirectories: true)
        let moved = try moveFiles(from: oldFolder, to: newFolder)

        do {
            try history.updateFilePaths(moved)
        } catch {
            undo(moved)
            throw WallpaperError("Couldn't update the history: \(error.localizedDescription) Nothing was changed.")
        }
        settings.update { $0.wallpaperFolder = Self.path(newFolder) }
        repointCurrentWallpaper(moved, fit: current.fit)
        if (try? fm.contentsOfDirectory(atPath: Self.path(oldFolder)))?.isEmpty == true {
            try? fm.removeItem(at: oldFolder) // the old folder goes once it's empty
        }

        Log.wallpaper.info("Moved \(moved.count) photo(s) to the new wallpaper folder")
        return moved.count
    }

    private func moveFiles(from: URL, to: URL) throws -> [String: String] {
        let fm = FileManager.default
        var moved: [String: String] = [:]
        guard let files = try? fm.contentsOfDirectory(at: from, includingPropertiesForKeys: nil) else { return moved }
        do {
            for file in files where file.pathExtension != "part" { // skip unfinished downloads
                let target = to.appending(path: file.lastPathComponent)
                try fm.moveItem(at: file, to: target) // copies and deletes across volumes
                // Keyed by the folder as configured (the listing may resolve symlinks like /var -> /private/var),
                // which is how history recorded the path.
                moved[Self.path(from.appending(path: file.lastPathComponent))] = Self.path(target)
            }
            return moved
        } catch {
            undo(moved)
            throw WallpaperError("Couldn't move the photos: \(error.localizedDescription) Nothing was changed.")
        }
    }

    private func undo(_ moved: [String: String]) {
        for (original, target) in moved {
            try? FileManager.default.moveItem(atPath: target, toPath: original)
        }
    }

    /// The desktop still points at the old files; set the same photos again from their new place.
    private func repointCurrentWallpaper(_ moved: [String: String], fit: WallpaperFit) {
        do {
            if let all = setter.current(on: nil) {
                if let new = moved[Self.path(all.standardizedFileURL)] { try setter.setForAll(URL(filePath: new), fit: fit) }
                return
            }
            for screen in setter.screens() {
                if let file = setter.current(on: screen.id), let new = moved[Self.path(file.standardizedFileURL)] {
                    try setter.set(URL(filePath: new), on: screen.id, fit: fit)
                }
            }
        } catch {
            Log.wallpaper.warning("Couldn't re-point the current wallpaper after moving the folder: \(error.localizedDescription, privacy: .public)")
        }
    }

    private static func path(_ url: URL) -> String {
        var p = url.path(percentEncoded: false)
        while p.count > 1 && p.hasSuffix("/") { p.removeLast() }
        return p
    }
}
