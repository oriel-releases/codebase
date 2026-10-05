using Oriel.Core.Models;
using Oriel.Core.Settings;

namespace Oriel.Core.Services;

public static class CandidateFilter
{
    /// <summary>Why a candidate was rejected, or null if it's fine.</summary>
    public static string? Reject(PhotoCandidate c, PixelSize target, OrielSettings s)
    {
        if (!c.IsLandscape) return "portrait";
        if (c.Width < target.Width || c.Height < target.Height) return "too small";
        if (s.SkipLowLikes && c.Likes < s.MinLikes) return "low likes";
        if (c.Description is { } d && s.ExcludedKeywords.FirstOrDefault(k =>
                !string.IsNullOrWhiteSpace(k) && d.Contains(k.Trim(), StringComparison.OrdinalIgnoreCase)) is { } hit)
            return $"excluded keyword '{hit}'";
        return null;
    }
}

public interface ITopicPicker
{
    /// <summary>Random enabled topic, avoiding <paramref name="avoid"/> when there's a choice.</summary>
    string Pick(IReadOnlyList<string> topics, ISet<string>? avoid = null);
}

public sealed class RandomTopicPicker(Random? random = null) : ITopicPicker
{
    readonly Random _random = random ?? Random.Shared;

    public const string Fallback = "landscape";

    public string Pick(IReadOnlyList<string> topics, ISet<string>? avoid = null)
    {
        if (topics.Count == 0) return Fallback;
        var pool = avoid is null ? topics : topics.Where(t => !avoid.Contains(t)).ToList();
        if (pool.Count == 0) pool = topics;
        return pool[_random.Next(pool.Count)];
    }
}
