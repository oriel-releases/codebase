using System.Text.Json;
using Oriel.Core.Models;

namespace Oriel.Core.Storage;

/// <summary>The position picked for each photo (the position editor here, or another device of the account).</summary>
public interface IPositionStore
{
    /// <summary>What was picked for this photo, or null when it follows the default (Smart).</summary>
    PhotoPosition? Get(string photoId);
    void Set(string photoId, PhotoPosition position);
}

/// <summary>
/// The picks in <c>positions.json</c>, newest last; only the last few hundred are kept (older photos are long gone from
/// the screen and History's files). Read once, then kept in memory.
/// </summary>
public sealed class JsonPositionStore(AppPaths paths) : IPositionStore
{
    const int Keep = 300;
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    readonly Lock _gate = new();
    List<Entry>? _entries;

    sealed record Entry(string Id, string Crop, double X, double Y);

    public PhotoPosition? Get(string photoId)
    {
        lock (_gate)
        {
            var entry = Entries().FindLast(e => e.Id == photoId);
            return entry is null ? null : PhotoPosition.FromSync(entry.Crop, entry.X, entry.Y);
        }
    }

    public void Set(string photoId, PhotoPosition position)
    {
        lock (_gate)
        {
            var entries = Entries();
            entries.RemoveAll(e => e.Id == photoId);
            entries.Add(new Entry(photoId, position.CropWord, position.X, position.Y));
            if (entries.Count > Keep) entries.RemoveRange(0, entries.Count - Keep);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.PositionsFile)!);
            var tmp = paths.PositionsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(entries, Options));
            File.Move(tmp, paths.PositionsFile, overwrite: true);
        }
    }

    List<Entry> Entries()
    {
        if (_entries is not null) return _entries;
        try
        {
            _entries = File.Exists(paths.PositionsFile)
                ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(paths.PositionsFile), Options) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A broken file only loses the picks: every photo goes back to the default.
            _entries = [];
        }
        return _entries;
    }
}
