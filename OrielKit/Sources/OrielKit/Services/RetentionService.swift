import Foundation

/// Deletes old non-favorite photos. Their rows stay in the database so photos are never reused.
public struct RetentionService: Sendable {
    /// Old non-favorite photos are deleted after this many days (sooner for a slideshow, which makes many).
    static let keepDays = 30
    static let slideshowKeepDays = 7

    private let history: HistoryStore
    private let settings: SettingsStore
    private let setter: WallpaperSetter
    private let clock: OrielClock

    public init(history: HistoryStore, settings: SettingsStore, setter: WallpaperSetter, clock: OrielClock = SystemClock()) {
        self.history = history
        self.settings = settings
        self.setter = setter
        self.clock = clock
    }

    /// Returns how many photos were removed.
    @discardableResult
    public func cleanup() -> Int {
        let days = settings.load().intervalMinutes > 0 ? Self.slideshowKeepDays : Self.keepDays
        let cutoff = clock.calendar.date(byAdding: .day, value: -days, to: clock.now)!
        let inUse = setter.filesInUse()
        var removed = 0
        for entry in history.expired(before: cutoff) {
            // Never delete what's on screen.
            if !entry.filePath.isEmpty && inUse.contains(entry.fileURL.standardizedFileURL.path(percentEncoded: false)) { continue }
            if entry.filePath.isEmpty || deleteFile(entry.fileURL) {
                history.markDeleted(entry.id)
                removed += 1
            }
        }
        if removed > 0 { Log.retention.info("Retention removed \(removed) old image(s)") }
        return removed
    }

    /// Delete from the History page.
    public func delete(_ id: Int64) {
        guard let entry = history.get(id) else { return }
        if !entry.filePath.isEmpty { _ = deleteFile(entry.fileURL) }
        history.markDeleted(id)
    }

    private func deleteFile(_ file: URL) -> Bool {
        // A favorite synced from another device and never downloaded here has no file ("" would mean the
        // current folder): nothing to delete.
        guard !file.path(percentEncoded: false).isEmpty, file.lastPathComponent.hasSuffix(".jpg") else { return true }
        do {
            try FileManager.default.removeItem(at: file)
            return true
        } catch CocoaError.fileNoSuchFile {
            return true
        } catch {
            Log.retention.warning("Couldn't delete \(file.lastPathComponent): \(error.localizedDescription)")
            return false
        }
    }
}
