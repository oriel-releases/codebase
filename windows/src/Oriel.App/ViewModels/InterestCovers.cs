namespace Oriel.UI.ViewModels;

/// <summary>
/// A photo for each built-in interest's tile (picked with extension/scripts/interest-covers.mjs, or from the extension's
/// bundled pool), shown straight from Unsplash like any other thumbnail.
/// </summary>
static class InterestCovers
{
    static readonly Dictionary<string, (string Path, string By)> Covers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nature"] = ("photo-1428908728789-d2de25dbd4e2?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk3fA", "Dominik Schröder"),
        ["mountains"] = ("34/BA1yLjNnQCI1yisIZGEi_2013-07-16_1922_IMG_9873.jpg?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk4fA", "Ryan Schroeder"),
        ["ocean"] = ("photo-1443397646383-16272048780e?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk4fA", "Mickey O'neil"),
        ["space"] = ("photo-1419242902214-272b3f66ee7a?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk5fA", "Vincentiu Solomon"),
        ["cities"] = ("photo-1417632993443-302f4897cf67?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk5fA", "Anders Jildén"),
        ["architecture"] = ("photo-1423768164017-3f27c066407f?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk5fA", "Anthony DELANOIX"),
        ["forest"] = ("photo-1415650328328-1909c4ceabdb?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NjAxfA", "Kristy Kravchenko"),
        ["desert"] = ("photo-1457264635001-828d0cbd483e?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NjAyfA", "Joshua Sortino"),
        ["aerial"] = ("photo-1454789591675-556c287e39e2?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NjAyfA", "NASA"),
        ["night sky"] = ("photo-1427501482951-3da9b725be23?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NjAzfA", "Michael Hull"),
        ["snow"] = ("reserve/unsplash_52cee6d9df810_1.JPG?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NjAzfA", "Caleb George"),
        ["beach"] = ("photo-1507525428034-b723cf961d3e?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8Mnx8YmVhY2h8ZW58MXwwfHx8MTc5MTgyNzc2MHww", "Sean Oulashin"),
        ["lakes"] = ("photo-1494500764479-0c8f2919a3d8?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8OHx8bGFrZXN8ZW58MXwwfHx8MTc5MTgyNzc2MXww", "Ken Cheung"),
        ["waterfalls"] = ("photo-1476610182048-b716b8518aae?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8Mnx8d2F0ZXJmYWxsc3xlbnwxfDB8fHwxNzkxODI3NzYyfDA", "Robert Lukeman"),
        ["autumn"] = ("photo-1523712999610-f77fbcfc3843?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MXx8YXV0dW1ufGVufDF8MHx8fDE3OTE4Mjc3NjJ8MA", "Johannes Plenio"),
        ["flowers"] = ("photo-1490750967868-88aa4486c946?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MXx8Zmxvd2Vyc3xlbnwxfDB8fHwxNzkxODI3NzYzfDA", "Sergey Shmidt"),
        ["northern lights"] = ("photo-1475518845976-0fd87b7e4e5d?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MTB8fG5vcnRoZXJuJTIwbGlnaHRzfGVufDF8MHx8fDE3OTE4Mjc3NjR8MA", "v2osk"),
        ["sunset"] = ("photo-1503803548695-c2a7b4a5b875?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8Mnx8c3Vuc2V0fGVufDF8MHx8fDE3OTE4Mjc3NjR8MA", "Sebastien Gabriel"),
        ["city at night"] = ("photo-1444723121867-7a241cacace9?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8NXx8Y2l0eSUyMGF0JTIwbmlnaHR8ZW58MXwwfHx8MTc5MTgyNzc2NXww", "Henning Witzel"),
        ["japan"] = ("photo-1466354424719-343280fe118b?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MTV8fGphcGFufGVufDF8MHx8fDE3OTE4Mjc3NjZ8MA", "Sora Sagano"),
        ["islands"] = ("photo-1502085671122-2d218cd434e6?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8NXx8aXNsYW5kc3xlbnwxfDB8fHwxNzkxODI3NzY2fDA", "Michael"),
        ["wildlife"] = ("photo-1504006833117-8886a355efbf?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MTR8fHdpbGRsaWZlfGVufDF8MHx8fDE3OTE4Mjc3Njd8MA", "Geranimo"),
        ["countryside"] = ("photo-1470240731273-7821a6eeb6bd?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MTF8fGNvdW50cnlzaWRlfGVufDF8MHx8fDE3OTE4Mjc3Njh8MA", "Joel Holland"),
        ["fog"] = ("photo-1504701954957-2010ec3bcec1?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MTR8fGZvZ3xlbnwxfDB8fHwxNzkxODI3NzY5fDA", "Stefano Pollio"),
    };

    /// <summary>The tile's photo, cropped 4:3 at this width; null for an interest without one.</summary>
    public static string? Url(string query, int width = 480) =>
        Covers.TryGetValue(query, out var c) ? $"https://images.unsplash.com/{c.Path}&w={width}&h={width * 3 / 4}&fit=crop&q=70&fm=jpg" : null;

    /// <summary>"Photo by Name on Unsplash" for the tile's tooltip, or "" without a photo.</summary>
    public static string Credit(string query) => Covers.TryGetValue(query, out var c) ? $"Photo by {c.By} on Unsplash" : "";
}
