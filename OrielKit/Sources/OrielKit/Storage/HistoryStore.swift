import Foundation

public protocol HistoryStore: Sendable {
    /// True if this photo was ever used, even if its file was deleted since.
    func wasUsed(provider: String, photoID: String) -> Bool
    /// Records a photo (or refreshes it when it was used before) and returns its row ID.
    @discardableResult func add(_ entry: HistoryEntry) throws -> Int64
    func list(includeDeleted: Bool) -> [HistoryEntry]
    func get(_ id: Int64) -> HistoryEntry?
    func setFavorite(_ id: Int64, _ favorite: Bool)
    func markDeleted(_ id: Int64)
    /// Non-favorite, not-yet-deleted entries applied before the cutoff.
    func expired(before cutoff: Date) -> [HistoryEntry]
    /// Points entries at their files' new locations (old path -> new path), all at once.
    func updateFilePaths(_ moves: [String: String]) throws
}

public extension HistoryStore {
    func list() -> [HistoryEntry] { list(includeDeleted: false) }
}

/// The history in oriel.db: every photo ever used, so none is repeated. Same table as the Windows app.
/// Deleting a photo only marks its row (soft delete), so "never repeat" still holds after cleanup.
/// Unchecked Sendable: the one connection is only ever used while holding `lock`.
public final class SQLiteHistoryStore: HistoryStore, @unchecked Sendable {
    private let lock = NSLock()
    private let database: SQLiteDatabase

    public convenience init(paths: AppPaths) throws {
        try self.init(file: paths.databaseFile)
    }

    public init(file: URL) throws {
        try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        let database = try SQLiteDatabase(path: file.path(percentEncoded: false))
        try database.execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS history (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                provider          TEXT NOT NULL,
                photo_id          TEXT NOT NULL,
                file_path         TEXT NOT NULL,
                thumbnail_url     TEXT NOT NULL,
                photo_page_url    TEXT NOT NULL,
                photographer_name TEXT NOT NULL,
                photographer_url  TEXT NOT NULL,
                description       TEXT,
                topic             TEXT,
                monitor_path      TEXT,
                applied_at        TEXT NOT NULL,
                is_favorite       INTEGER NOT NULL DEFAULT 0,
                is_deleted        INTEGER NOT NULL DEFAULT 0,
                UNIQUE (provider, photo_id)
            );
            CREATE INDEX IF NOT EXISTS ix_history_applied ON history (applied_at);
            """)
        self.database = database
    }

    public func wasUsed(provider: String, photoID: String) -> Bool {
        let rows = guarded([Int64]()) { db in
            try db.query("SELECT 1 AS used FROM history WHERE provider = ? AND photo_id = ? LIMIT 1",
                         [.text(provider), .text(photoID)]) { $0.int("used") }
        }
        return !rows.isEmpty
    }

    @discardableResult
    public func add(_ e: HistoryEntry) throws -> Int64 {
        try withDatabase { db in
            try db.run("""
                INSERT INTO history (provider, photo_id, file_path, thumbnail_url, photo_page_url,
                    photographer_name, photographer_url, description, topic, monitor_path, applied_at, is_favorite)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ON CONFLICT (provider, photo_id) DO UPDATE SET file_path = excluded.file_path,
                    topic = excluded.topic, applied_at = excluded.applied_at, is_deleted = 0
                """, [
                    .text(e.provider), .text(e.photoID), .text(e.filePath), .text(e.thumbnailURL), .text(e.photoPageURL),
                    .text(e.photographerName), .text(e.photographerURL), .optional(e.description), .optional(e.topic),
                    .optional(e.screenID), .text(Self.format(e.appliedAt)), .int(e.isFavorite ? 1 : 0),
                ])
            let ids = try db.query("SELECT id FROM history WHERE provider = ? AND photo_id = ?",
                                   [.text(e.provider), .text(e.photoID)]) { $0.int("id") }
            return ids[0]
        }
    }

    public func list(includeDeleted: Bool) -> [HistoryEntry] {
        entries(includeDeleted
            ? "SELECT * FROM history ORDER BY applied_at DESC, id DESC"
            : "SELECT * FROM history WHERE is_deleted = 0 ORDER BY applied_at DESC, id DESC")
    }

    public func get(_ id: Int64) -> HistoryEntry? {
        entries("SELECT * FROM history WHERE id = ?", [.int(id)]).first
    }

    public func setFavorite(_ id: Int64, _ favorite: Bool) {
        guarded(()) { try $0.run("UPDATE history SET is_favorite = ? WHERE id = ?", [.int(favorite ? 1 : 0), .int(id)]) }
    }

    public func markDeleted(_ id: Int64) {
        guarded(()) { try $0.run("UPDATE history SET is_deleted = 1 WHERE id = ?", [.int(id)]) }
    }

    public func expired(before cutoff: Date) -> [HistoryEntry] {
        entries("SELECT * FROM history WHERE is_favorite = 0 AND is_deleted = 0 AND applied_at < ?",
                [.text(Self.format(cutoff))])
    }

    public func updateFilePaths(_ moves: [String: String]) throws {
        guard !moves.isEmpty else { return }
        try withDatabase { db in
            try db.transaction {
                for (old, new) in moves {
                    try db.run("UPDATE history SET file_path = ? WHERE file_path = ?", [.text(new), .text(old)])
                }
            }
        }
    }

    private func entries(_ sql: String, _ args: [SQLiteDatabase.Value] = []) -> [HistoryEntry] {
        guarded([]) { db in
            try db.query(sql, args) { r in
                HistoryEntry(
                    id: r.int("id"),
                    provider: r.string("provider"),
                    photoID: r.string("photo_id"),
                    filePath: r.string("file_path"),
                    thumbnailURL: r.string("thumbnail_url"),
                    photoPageURL: r.string("photo_page_url"),
                    photographerName: r.string("photographer_name"),
                    photographerURL: r.string("photographer_url"),
                    description: r.optionalString("description"),
                    topic: r.optionalString("topic"),
                    screenID: r.optionalString("monitor_path"),
                    appliedAt: Self.parse(r.string("applied_at")) ?? .distantPast,
                    isFavorite: r.int("is_favorite") != 0,
                    isDeleted: r.int("is_deleted") != 0)
            }
        }
    }

    /// Runs a read or a best-effort write; a database error is logged and `fallback` returned.
    @discardableResult
    private func guarded<T>(_ fallback: T, _ body: (SQLiteDatabase) throws -> T) -> T {
        do {
            return try withDatabase(body)
        } catch {
            Log.history.error("History database: \(String(describing: error), privacy: .public)")
            return fallback
        }
    }

    private func withDatabase<T>(_ body: (SQLiteDatabase) throws -> T) throws -> T {
        lock.lock()
        defer { lock.unlock() }
        return try body(database)
    }

    // Stored as UTC ISO 8601 with milliseconds: text order is time order, so SQL can compare and sort it.
    private static func format(_ date: Date) -> String {
        Date.ISO8601FormatStyle(includingFractionalSeconds: true).format(date)
    }

    private static func parse(_ text: String) -> Date? {
        (try? Date.ISO8601FormatStyle(includingFractionalSeconds: true).parse(text))
            ?? (try? Date.ISO8601FormatStyle().parse(text))
    }
}
