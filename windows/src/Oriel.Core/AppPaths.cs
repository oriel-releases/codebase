namespace Oriel.Core;

/// <summary>Where Oriel keeps its files. Overridable so tests can use a temp folder.</summary>
public sealed class AppPaths
{
    public AppPaths(string? dataRoot = null, string? picturesRoot = null)
    {
        DataRoot = dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Oriel");
        PicturesRoot = picturesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Oriel");
    }

    public string DataRoot { get; }
    /// <summary>Default wallpaper folder; the user can pick another one (see <see cref="WallpaperFolder"/>).</summary>
    public string PicturesRoot { get; }

    public string WallpaperFolder(Settings.OrielSettings settings) =>
        string.IsNullOrWhiteSpace(settings.WallpaperFolder) ? PicturesRoot : settings.WallpaperFolder;

    public string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public string AccountFile => Path.Combine(DataRoot, "auth.bin");
    public string StateFile => Path.Combine(DataRoot, "state.json");
    public string DatabaseFile => Path.Combine(DataRoot, "oriel.db");
    public string LockFile => Path.Combine(DataRoot, "apply.lock");
    public string LogsDir => Path.Combine(DataRoot, "logs");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogsDir);
    }
}
