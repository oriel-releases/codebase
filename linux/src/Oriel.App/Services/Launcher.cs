using Oriel.Core.Platform;

namespace Oriel.UI.Services;

/// <summary>Opens links and folders in the user's default apps.</summary>
static class Launcher
{
    public static bool OpenUrl(Uri uri) =>
        uri.Scheme is "http" or "https" && Proc.Start("xdg-open", uri.AbsoluteUri);

    public static void OpenFolder(string dir)
    {
        Directory.CreateDirectory(dir);
        Proc.Start("xdg-open", dir);
    }

    /// <summary>Opens the file manager with the file selected (the FileManager1 D-Bus API), or just its folder.</summary>
    public static void ShowInFileManager(string file)
    {
        if (File.Exists(file))
        {
            var uri = new Uri(file).AbsoluteUri.Replace("'", "%27");
            if (Proc.Has("gdbus") && Proc.Run("gdbus", "call", "--session", "--dest", "org.freedesktop.FileManager1",
                    "--object-path", "/org/freedesktop/FileManager1", "--method", "org.freedesktop.FileManager1.ShowItems",
                    $"['{uri}']", "") is { Ok: true })
                return;
        }
        if (Path.GetDirectoryName(file) is { } dir && Directory.Exists(dir)) OpenFolder(dir);
    }
}
