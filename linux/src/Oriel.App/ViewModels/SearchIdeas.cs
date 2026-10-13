namespace Oriel.UI.ViewModels;

/// <summary>Ideas for "Try searching for": a few at random each time, so the start of Search keeps changing.</summary>
static class SearchIdeas
{
    static readonly string[] All =
    [
        // Mountains and hills
        "snowy mountains", "alpine lake", "dolomites", "patagonia", "swiss alps", "himalayas", "mountain sunrise", "volcano",
        "rolling hills", "tuscany hills", "scottish highlands", "mount fuji", "rocky mountains", "mountain cabin", "glacier", "canyon",
        "grand canyon", "fjord", "norway fjords", "iceland landscape",
        // Water
        "calm ocean", "ocean waves", "turquoise water", "tropical beach", "lighthouse", "waterfall", "misty lake", "river valley",
        "coral reef", "underwater", "sailboat", "rainy window", "rain drops", "ice cave", "frozen lake", "black sand beach",
        "cliffs by the sea", "harbor at dawn", "lagoon", "surf",
        // Forests and plants
        "foggy forest", "pine forest", "autumn leaves", "bamboo forest", "cherry blossoms", "lavender field", "sunflower field",
        "tulips", "moss", "ferns", "redwoods", "birch trees", "jungle", "rainforest", "wildflowers", "olive trees", "palm trees",
        "botanical garden", "succulents", "monstera",
        // Sky and space
        "northern lights", "milky way", "starry sky", "full moon", "galaxy", "nebula", "earth from space", "sunset clouds",
        "golden hour", "blue hour", "storm clouds", "lightning", "rainbow", "solar eclipse", "night sky desert", "cotton candy sky",
        "sunrise", "pink sky", "comet", "planets",
        // Cities
        "tokyo night", "city skyline", "new york", "paris streets", "london rain", "hong kong", "dubai skyline", "seoul at night",
        "venice canals", "amsterdam", "istanbul", "rooftops", "neon signs", "city lights from above", "empty street",
        "subway station", "bridge at night", "old town", "cafe street", "tram",
        // Architecture
        "brutalism", "modern architecture", "spiral staircase", "minimal building", "glass building", "cathedral", "temple",
        "mosque", "japanese temple", "old library", "arches", "concrete", "interior design", "scandinavian interior",
        "geometric architecture", "skyscraper", "lighthouse at night", "windmill", "castle", "ruins",
        // Deserts and dry places
        "desert dunes", "sahara", "joshua tree", "salt flats", "red rocks", "monument valley", "oasis", "cactus", "badlands",
        "desert road", "wadi rum", "namib desert", "sandstone", "dry lake", "atacama",
        // Snow and winter
        "snowy forest", "winter village", "frost", "snowfall", "ice", "polar bear", "snowy cabin", "winter road", "ski slope",
        "arctic", "antarctica", "frozen waterfall", "winter sunrise", "snowy pines", "christmas lights",
        // Animals
        "fox", "deer in forest", "wolf", "owl", "whale", "jellyfish", "horses", "cat", "dog", "birds flying", "eagle", "lion",
        "elephant", "butterfly", "koi fish",
        // Minimal, dark, abstract
        "minimal landscape", "black and white", "dark moody", "pastel", "gradient", "abstract shapes", "texture", "marble",
        "paper texture", "smoke", "ink in water", "bokeh", "long exposure", "light trails", "neon", "geometry", "shadows",
        "silhouette", "fog", "monochrome",
        // Aerial and roads
        "aerial beach", "aerial forest", "drone city", "road trip", "winding road", "aerial river", "farmland from above",
        "rice terraces", "salt pans", "highway at night",
        // Seasons and moods
        "spring morning", "summer evening", "autumn forest", "cozy", "calm", "dreamy", "vintage", "film photography", "sunlight through trees",
        "golden fields", "lonely tree", "countryside", "village", "picnic", "hot air balloons", "cappadocia", "santorini", "bali",
        "kyoto", "morocco",
    ];

    /// <summary><paramref name="count"/> ideas in random order, no repeats.</summary>
    public static IReadOnlyList<string> Pick(int count = 8) => All.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();

    public static int Count => All.Length;
}
