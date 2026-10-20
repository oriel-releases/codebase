namespace Oriel.UI.ViewModels;

/// <summary>A look for an AI wallpaper: words added after the user's own.</summary>
public sealed record CreateStyle(string Label, string? Words);

/// <summary>Ideas and looks for the Create page. A few ideas at random each time, like Search.</summary>
static class CreateIdeas
{
    public static readonly CreateStyle[] Styles =
    [
        new("Any", null),
        new("Photo", "photorealistic, natural light, sharp detail"),
        new("Painting", "oil painting, visible brush strokes, rich colors"),
        new("Watercolor", "soft watercolor painting, flowing pigments"),
        new("Anime", "anime background art, soft light, detailed scenery"),
        new("Minimal", "minimalist, flat colors, simple shapes, lots of empty space"),
        new("Abstract", "abstract art, flowing shapes, smooth gradients"),
        new("3D", "3D render, soft studio lighting, clay look"),
        new("Pixel art", "pixel art, retro game scene"),
    ];

    static readonly string[] All =
    [
        "a quiet lake at dawn with mist and mountains", "a cozy cabin in a snowy forest at night",
        "a lighthouse on a cliff during a storm", "a field of lavender under a pink sky",
        "a tiny island with one palm tree in a turquoise sea", "a train crossing a bridge through autumn mountains",
        "a whale swimming through clouds", "a city of glass towers floating above the sea",
        "a desert with giant moons in the sky", "a Japanese street with lanterns in the rain",
        "a library inside a giant tree", "a greenhouse full of plants and warm light",
        "the northern lights over a frozen lake", "a sunflower field at golden hour",
        "a calm ocean seen from a sailboat at sunset", "a waterfall in a jungle full of birds",
        "a fox sleeping in a mossy forest", "an astronaut fishing on the moon",
        "a castle on a mountain above the clouds", "a neon city at night after rain",
        "a hot air balloon festival over green hills", "a koi pond with lily pads from above",
        "a road trip through red rock canyons", "a cat looking at the stars from a rooftop",
        "an underwater city with glowing jellyfish", "cherry blossoms along a river in spring",
        "a mountain village with snow and warm windows", "planets rising over an alien ocean",
        "a paper boat on a puddle reflecting the sky", "a lone tree on a hill under the Milky Way",
        "a cozy reading nook by a rainy window", "waves of colorful sand dunes",
    ];

    public static IEnumerable<string> Pick(int count = 6) => All.OrderBy(_ => Random.Shared.Next()).Take(count);

    /// <summary>One idea at random ("I'm feeling lucky").</summary>
    public static string Any() => All[Random.Shared.Next(All.Length)];
}
