namespace Oriel.Core.Wallpaper;

/// <summary>Finds a photo's accent color. Lives in the app project: it needs that platform's image decoder.</summary>
public interface IAccentSampler
{
    /// <summary>The photo's accent color as "#RRGGBB", or null when it can't be read.</summary>
    Task<string?> SampleAsync(string imagePath);
}
