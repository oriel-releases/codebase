using Oriel.Core.Wallpaper;
using Windows.Storage;
using Windows.System.UserProfile;

namespace Oriel.UI.Services;

public sealed class LockScreenSetter : ILockScreenSetter
{
    public async Task<bool> TrySetAsync(string imagePath)
    {
        var file = await StorageFile.GetFileFromPathAsync(imagePath);
        try
        {
            // UserProfilePersonalizationSettings.TrySetLockScreenImageAsync returns false for
            // unpackaged apps; the older LockScreen API works and throws on failure.
            await LockScreen.SetImageFileAsync(file);
            return true;
        }
        catch (Exception) when (UserProfilePersonalizationSettings.IsSupported())
        {
            return await UserProfilePersonalizationSettings.Current.TrySetLockScreenImageAsync(file);
        }
    }
}
