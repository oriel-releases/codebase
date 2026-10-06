namespace Oriel.Core.Wallpaper;

/// <summary>Sets the lock screen picture, on the desktops that let an app do that.</summary>
public interface ILockScreenSetter
{
    /// <summary>Best effort: false when this desktop doesn't allow it.</summary>
    Task<bool> TrySetAsync(string imagePath);
}
