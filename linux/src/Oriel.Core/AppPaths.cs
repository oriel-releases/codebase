namespace Oriel.Core;

/// <summary>Where Oriel keeps its files (XDG base directories). Overridable so tests can use a temp folder.</summary>
public sealed class AppPaths
{
    public AppPaths(string? dataRoot = null, string? picturesRoot = null)
    {
        DataRoot = dataRoot ?? Path.Combine(XdgDataHome(), "oriel");
        PicturesRoot = picturesRoot ?? Path.Combine(PicturesDir(), "Oriel");
    }

    public string DataRoot { get; }
    /// <summary>Default wallpaper folder; the user can pick another one (see <see cref="WallpaperFolder"/>).</summary>
    public string PicturesRoot { get; }

    public string WallpaperFolder(Settings.OrielSettings settings) =>
        string.IsNullOrWhiteSpace(settings.WallpaperFolder) ? PicturesRoot : settings.WallpaperFolder;

    public string SettingsFile => Path.Combine(DataRoot, "settings.json");
    /// <summary>Fallback for the account when no system keyring is available. Readable by this user only.</summary>
    public string AccountFile => Path.Combine(DataRoot, "auth.json");
    public string StateFile => Path.Combine(DataRoot, "state.json");
    public string DatabaseFile => Path.Combine(DataRoot, "oriel.db");
    public string LockFile => Path.Combine(DataRoot, "apply.lock");
    /// <summary>The image Oriel set last, as a plain path: most desktops can't be asked what they show.</summary>
    public string CurrentWallpaperFile => Path.Combine(DataRoot, "current-wallpaper.txt");
    public string LogsDir => Path.Combine(DataRoot, "logs");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogsDir);
    }

    public static string Home => Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } h
        ? h : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string XdgDataHome() => Env("XDG_DATA_HOME") ?? Path.Combine(Home, ".local", "share");
    public static string XdgConfigHome() => Env("XDG_CONFIG_HOME") ?? Path.Combine(Home, ".config");

    /// <summary>The user's Pictures folder from <c>user-dirs.dirs</c> (it can be localized), else ~/Pictures.</summary>
    public static string PicturesDir()
    {
        try
        {
            var file = Path.Combine(XdgConfigHome(), "user-dirs.dirs");
            if (File.Exists(file))
                foreach (var line in File.ReadLines(file))
                {
                    var t = line.Trim();
                    if (!t.StartsWith("XDG_PICTURES_DIR=", StringComparison.Ordinal)) continue;
                    var value = t["XDG_PICTURES_DIR=".Length..].Trim().Trim('"');
                    value = value.Replace("$HOME", Home);
                    if (Path.IsPathRooted(value)) return value;
                }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Path.Combine(Home, "Pictures");
    }

    static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v && Path.IsPathRooted(v) ? v : null;
}
