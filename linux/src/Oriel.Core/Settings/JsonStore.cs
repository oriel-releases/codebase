using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oriel.Core.Settings;

public interface ISettingsStore
{
    OrielSettings Load();
    void Save(OrielSettings settings);
    OrielState LoadState();
    void SaveState(OrielState state);
    event EventHandler? SettingsChanged;
}

public sealed class JsonSettingsStore(AppPaths paths) : ISettingsStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    readonly Lock _gate = new();

    public event EventHandler? SettingsChanged;

    public OrielSettings Load()
    {
        var settings = Read<OrielSettings>(paths.SettingsFile);
        settings.UpdateBuiltInTopics();
        return settings;
    }

    public void Save(OrielSettings settings)
    {
        Write(paths.SettingsFile, settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public OrielState LoadState() => Read<OrielState>(paths.StateFile);

    public void SaveState(OrielState state) => Write(paths.StateFile, state);

    T Read<T>(string file) where T : new()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(file)) return new T();
                return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Options) ?? new T();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A corrupt file shouldn't brick the app; fall back to defaults.
                return new T();
            }
        }
    }

    void Write<T>(string file, T value)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, file, overwrite: true);
        }
    }
}

public static class SettingsStoreExtensions
{
    /// <summary>Load-modify-save, so different screens never overwrite each other's changes.</summary>
    public static OrielSettings Update(this ISettingsStore store, Action<OrielSettings> change)
    {
        var s = store.Load();
        change(s);
        store.Save(s);
        return s;
    }
}
