using System.Web;

namespace Oriel.Core.Auth;

/// <summary>The <c>oriel://auth?token=...</c> link the website opens after Google sign-in.</summary>
public static partial class DeepLink
{
    /// <summary>Finds an auth link among command-line arguments or a launch argument string.</summary>
    public static bool TryGetGoogleToken(IEnumerable<string> args, out string token)
    {
        token = "";
        foreach (var arg in args)
        {
            var candidate = arg.Trim().Trim('"');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) continue;
            if (!uri.Scheme.Equals("oriel", StringComparison.OrdinalIgnoreCase) ||
                !uri.Host.Equals("auth", StringComparison.OrdinalIgnoreCase)) continue;
            var value = HttpUtility.ParseQueryString(uri.Query)["token"];
            if (string.IsNullOrWhiteSpace(value)) continue;
            token = value;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The <c>oriel://wallpaper?id=PHOTO_ID</c> link on the website's share page: a photo someone shared, to set as the wallpaper.
    /// Only a plain Unsplash photo ID is accepted.
    /// </summary>
    public static bool TryGetSharedPhoto(IEnumerable<string> args, out string photoId) =>
        TryGetSharedPhoto(args, out photoId, out _);

    /// <summary>Same, plus the sender's first name when the link has one (empty otherwise).</summary>
    public static bool TryGetSharedPhoto(IEnumerable<string> args, out string photoId, out string from)
    {
        photoId = "";
        from = "";
        foreach (var arg in args)
        {
            if (!Uri.TryCreate(arg.Trim().Trim('"'), UriKind.Absolute, out var uri)) continue;
            if (!uri.Scheme.Equals("oriel", StringComparison.OrdinalIgnoreCase) ||
                !uri.Host.Equals("wallpaper", StringComparison.OrdinalIgnoreCase)) continue;
            var id = HttpUtility.ParseQueryString(uri.Query)["id"];
            if (id is null || !ValidPhotoId().IsMatch(id)) continue;
            photoId = id;
            from = Models.ShareLink.CleanName(HttpUtility.ParseQueryString(uri.Query)["from"]);
            return true;
        }
        return false;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_-]{6,24}$")]
    private static partial System.Text.RegularExpressions.Regex ValidPhotoId();

    /// <summary>
    /// A shared photo carried through sign-in: someone opened a share link, installed Oriel and signed in, and the
    /// website added the photo to the <c>oriel://auth</c> link (<c>&amp;share=ID&amp;from=Name</c>), so it isn't lost.
    /// </summary>
    public static bool TryGetPendingShare(IEnumerable<string> args, out string photoId, out string from)
    {
        photoId = "";
        from = "";
        foreach (var arg in args)
        {
            if (!Uri.TryCreate(arg.Trim().Trim('"'), UriKind.Absolute, out var uri)) continue;
            if (!uri.Scheme.Equals("oriel", StringComparison.OrdinalIgnoreCase) ||
                !uri.Host.Equals("auth", StringComparison.OrdinalIgnoreCase)) continue;
            var query = HttpUtility.ParseQueryString(uri.Query);
            if (query["share"] is not { } id || !ValidPhotoId().IsMatch(id)) continue;
            photoId = id;
            from = Models.ShareLink.CleanName(query["from"]);
            return true;
        }
        return false;
    }

    /// <summary>True for <c>oriel://open</c>, which just brings Oriel to the front (the browser extension's "Open Oriel" button).</summary>
    public static bool IsOpen(IEnumerable<string> args) =>
        args.Any(a => Uri.TryCreate(a.Trim().Trim('"'), UriKind.Absolute, out var uri) &&
                      uri.Scheme.Equals("oriel", StringComparison.OrdinalIgnoreCase) &&
                      uri.Host.Equals("open", StringComparison.OrdinalIgnoreCase));

    /// <summary>True if any argument is an oriel:// link, whether or not it carries a usable token.</summary>
    public static bool IsLink(IEnumerable<string> args) =>
        args.Any(a => a.Trim().Trim('"').StartsWith("oriel://", StringComparison.OrdinalIgnoreCase));
}
