namespace Oriel.Core.Wallpaper;

/// <summary>Sets the Windows lock screen picture. Lives in the app project: it needs the WinRT APIs.</summary>
public interface ILockScreenSetter
{
    /// <summary>Best effort: false when Windows doesn't allow it.</summary>
    Task<bool> TrySetAsync(string imagePath);
}
