namespace Oriel.Core.Settings;

/// <summary>One time zone in the picker: its IANA name (what syncs) and "Tashkent (UTC+05:00)".</summary>
public sealed record ZoneChoice(string Id, string Label);

/// <summary>
/// The time zones to pick from for the shared schedule, by IANA name like the phone's picker (Windows' own zones are
/// turned into their IANA names), sorted by city.
/// </summary>
public static class TimeZones
{
    public static IReadOnlyList<ZoneChoice> All(string? keep = null)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            var id = zone.Id.Contains('/') ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : null;
            if (id is not null && id.Contains('/') && !id.StartsWith("Etc/", StringComparison.Ordinal)) ids.Add(id);
        }
        // The account's zone stays pickable even when this computer doesn't list it.
        if (!string.IsNullOrEmpty(keep)) ids.Add(keep);
        return ids.Select(id => new ZoneChoice(id, Label(id))).OrderBy(z => z.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>"Asia/Tashkent" → "Tashkent (UTC+05:00)".</summary>
    public static string Label(string id)
    {
        var city = id[(id.LastIndexOf('/') + 1)..].Replace('_', ' ');
        try
        {
            var offset = TimeZoneInfo.FindSystemTimeZoneById(id).GetUtcOffset(DateTime.UtcNow);
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            return $"{city} (UTC{sign}{offset.Duration():hh\\:mm})";
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return city;
        }
    }
}
