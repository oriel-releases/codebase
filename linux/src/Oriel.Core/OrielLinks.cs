namespace Oriel.Core;

/// <summary>Web addresses the app points people to.</summary>
public static class OrielLinks
{
    /// <summary>The Telegram support bot: questions, bugs and ideas.</summary>
    public const string Support = "https://t.me/oriel_support_bot";

    /// <summary>The website's account page: profile, current wallpaper and where you're signed in (it asks you to sign in first).</summary>
    public const string Account = "https://oriel.akbarjon.uz/me";

    /// <summary>The website's share pages (<c>/share/{id}</c>): one click there opens Oriel and sets the photo.</summary>
    public const string Share = "https://oriel.akbarjon.uz/share";

    /// <summary>The website's follow pages (<c>/follow/{code}</c>): one click there opens Oriel and follows.</summary>
    public const string Follow = "https://oriel.akbarjon.uz/follow";

    /// <summary>The website's privacy page.</summary>
    public const string Privacy = "https://oriel.akbarjon.uz/privacy";

    /// <summary>Unsplash, where the photos come from (credited as their guidelines ask).</summary>
    public const string Unsplash = "https://unsplash.com/?utm_source=oriel&utm_medium=referral";

    /// <summary>Pollinations, which makes the AI wallpapers.</summary>
    public const string Pollinations = "https://pollinations.ai/";

    /// <summary>The Wallpaper of the day list the website publishes for the apps (see TodayService).</summary>
    public const string TodayList = "https://oriel.akbarjon.uz/today.json";

    /// <summary>This hour's Explore photos, rebuilt every hour by the public oriel-releases/explore repo (see ExploreService).</summary>
    public const string ExploreFeed = "https://raw.githubusercontent.com/oriel-releases/explore/feed/explore.json";
}
