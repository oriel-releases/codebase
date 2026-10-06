using System.Reflection;

namespace Oriel.UI.Services;

static class AppInfo
{
    /// <summary>
    /// What to launch to start Oriel again. Inside an AppImage the running binary sits in a temporary mount
    /// that disappears on exit; the AppImage file itself is what stays.
    /// </summary>
    public static string ExePath =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image && File.Exists(image)
            ? image
            : Environment.ProcessPath!;

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>"Beta" while the csproj says so (AssemblyMetadata Stage), otherwise empty.</summary>
    public static string Stage => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "Stage")?.Value ?? "";

    /// <summary>The icon shipped with the app, used for the tray and copied for the menu entry.</summary>
    public static string IconFile => Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");
}
