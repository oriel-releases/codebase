using Oriel.Core.Models;
using Microsoft.Data.Sqlite;

namespace Oriel.Core.Storage;

public interface IHistoryStore
{
    /// <summary>True if this photo was ever used, even if its file was deleted since.</summary>
    bool WasUsed(string provider, string photoId);
    long Add(HistoryEntry entry);
    IReadOnlyList<HistoryEntry> List(bool includeDeleted = false);
    HistoryEntry? Get(long id);
    void SetFavorite(long id, bool favorite);
    void MarkDeleted(long id);
    /// <summary>Non-favorite, not-yet-deleted entries applied before the cutoff.</summary>
    IReadOnlyList<HistoryEntry> ExpiredBefore(DateTimeOffset cutoff);
    /// <summary>Points entries at their files' new locations (old path -> new path), in one transaction.</summary>
    void UpdateFilePaths(IReadOnlyDictionary<string, string> moves);
}

public sealed class SqliteHistoryStore : IHistoryStore
{
    readonly string _connectionString;

    public SqliteHistoryStore(AppPaths paths) : this(paths.DatabaseFile) { }

    public SqliteHistoryStore(string databaseFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databaseFile))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFile,
            Pooling = false, // don't hold the file open; the CLI and the app may both touch it
        }.ToString();
        Migrate();
    }

    SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return c;
    }

    void Migrate()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
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
            """;
        cmd.ExecuteNonQuery();
    }

    public bool WasUsed(string provider, string photoId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM history WHERE provider = $p AND photo_id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$p", provider);
        cmd.Parameters.AddWithValue("$id", photoId);
        return cmd.ExecuteScalar() is not null;
    }

    public long Add(HistoryEntry e)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO history (provider, photo_id, file_path, thumbnail_url, photo_page_url,
                photographer_name, photographer_url, description, topic, monitor_path, applied_at, is_favorite)
            VALUES ($provider, $photo_id, $file_path, $thumb, $page, $pname, $purl, $desc, $topic, $mon, $at, $fav)
            ON CONFLICT (provider, photo_id) DO UPDATE SET file_path = excluded.file_path,
                topic = excluded.topic, applied_at = excluded.applied_at, is_deleted = 0;
            SELECT id FROM history WHERE provider = $provider AND photo_id = $photo_id;
            """;
        cmd.Parameters.AddWithValue("$provider", e.Provider);
        cmd.Parameters.AddWithValue("$photo_id", e.PhotoId);
        cmd.Parameters.AddWithValue("$file_path", e.FilePath);
        cmd.Parameters.AddWithValue("$thumb", e.ThumbnailUrl);
        cmd.Parameters.AddWithValue("$page", e.PhotoPageUrl);
        cmd.Parameters.AddWithValue("$pname", e.PhotographerName);
        cmd.Parameters.AddWithValue("$purl", e.PhotographerUrl);
        cmd.Parameters.AddWithValue("$desc", (object?)e.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$topic", (object?)e.Topic ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mon", (object?)e.MonitorPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", e.AppliedAt.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$fav", e.IsFavorite ? 1 : 0);
        return (long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<HistoryEntry> List(bool includeDeleted = false) =>
        Query(includeDeleted
            ? "SELECT * FROM history ORDER BY applied_at DESC, id DESC"
            : "SELECT * FROM history WHERE is_deleted = 0 ORDER BY applied_at DESC, id DESC");

    public HistoryEntry? Get(long id) =>
        Query("SELECT * FROM history WHERE id = $id", ("$id", id)).FirstOrDefault();

    public void SetFavorite(long id, bool favorite) =>
        Exec("UPDATE history SET is_favorite = $f WHERE id = $id", ("$f", favorite ? 1 : 0), ("$id", id));

    public void MarkDeleted(long id) =>
        Exec("UPDATE history SET is_deleted = 1 WHERE id = $id", ("$id", id));

    public IReadOnlyList<HistoryEntry> ExpiredBefore(DateTimeOffset cutoff) =>
        Query("SELECT * FROM history WHERE is_favorite = 0 AND is_deleted = 0 AND applied_at < $cutoff",
            ("$cutoff", cutoff.ToUniversalTime().ToString("O")));

    public void UpdateFilePaths(IReadOnlyDictionary<string, string> moves)
    {
        if (moves.Count == 0) return;
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE history SET file_path = $new WHERE file_path = $old";
        var oldParam = cmd.Parameters.Add("$old", SqliteType.Text);
        var newParam = cmd.Parameters.Add("$new", SqliteType.Text);
        foreach (var (oldPath, newPath) in moves)
        {
            oldParam.Value = oldPath;
            newParam.Value = newPath;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    void Exec(string sql, params (string Name, object Value)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    List<HistoryEntry> Query(string sql, params (string Name, object Value)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        using var r = cmd.ExecuteReader();
        var list = new List<HistoryEntry>();
        while (r.Read())
        {
            string? Opt(string col) => r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetString(r.GetOrdinal(col));
            list.Add(new HistoryEntry
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                Provider = r.GetString(r.GetOrdinal("provider")),
                PhotoId = r.GetString(r.GetOrdinal("photo_id")),
                FilePath = r.GetString(r.GetOrdinal("file_path")),
                ThumbnailUrl = r.GetString(r.GetOrdinal("thumbnail_url")),
                PhotoPageUrl = r.GetString(r.GetOrdinal("photo_page_url")),
                PhotographerName = r.GetString(r.GetOrdinal("photographer_name")),
                PhotographerUrl = r.GetString(r.GetOrdinal("photographer_url")),
                Description = Opt("description"),
                Topic = Opt("topic"),
                MonitorPath = Opt("monitor_path"),
                AppliedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("applied_at")), null,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                IsFavorite = r.GetInt64(r.GetOrdinal("is_favorite")) != 0,
                IsDeleted = r.GetInt64(r.GetOrdinal("is_deleted")) != 0,
            });
        }
        return list;
    }
}
