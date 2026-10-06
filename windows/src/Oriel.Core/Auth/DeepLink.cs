using System.Web;

namespace Oriel.Core.Auth;

/// <summary>The <c>oriel://auth?token=...</c> link the website opens after Google sign-in.</summary>
public static class DeepLink
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

    /// <summary>True for <c>oriel://open</c>, which just brings Oriel to the front (the browser extension's "Open Oriel" button).</summary>
    public static bool IsOpen(IEnumerable<string> args) =>
        args.Any(a => Uri.TryCreate(a.Trim().Trim('"'), UriKind.Absolute, out var uri) &&
                      uri.Scheme.Equals("oriel", StringComparison.OrdinalIgnoreCase) &&
                      uri.Host.Equals("open", StringComparison.OrdinalIgnoreCase));

    /// <summary>True if any argument is an oriel:// link, whether or not it carries a usable token.</summary>
    public static bool IsLink(IEnumerable<string> args) =>
        args.Any(a => a.Trim().Trim('"').StartsWith("oriel://", StringComparison.OrdinalIgnoreCase));
}
