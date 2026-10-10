namespace Oriel.Core.Wallpaper;

/// <summary>Changes the Windows accent color to match the wallpaper. Lives in the app project: it writes the registry.</summary>
public interface IAccentSetter
{
    /// <summary>Call after the wallpaper is set. Best effort.</summary>
    Task ApplyAsync(string imagePath);
}
