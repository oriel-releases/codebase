namespace Oriel.Core.Platform;

/// <summary>
/// A global shortcut like "Ctrl+Alt+W": modifier flags plus one key by name. The text form is what the
/// settings file keeps. Key names double as X11 keysym names where they differ only by case.
/// </summary>
public readonly record struct Hotkey(uint Modifiers, string Key)
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Super = 0x8;

    static readonly string[] Named = ["Space", "PageUp", "PageDown", "End", "Home", "Left", "Up", "Right", "Down", "Insert", "Delete"];

    /// <summary>The canonical name for a key we allow in a shortcut (letters, digits, F1-F24 and a few more), or null.</summary>
    public static string? NormalizeKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])) return name.ToUpperInvariant();
        if ((name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out var f) && f is >= 1 and <= 24) return $"F{f}";
        return Named.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The X11 keysym name of the key ("a", "F5", "space", "Prior").</summary>
    public string KeysymName => Key switch
    {
        "PageUp" => "Prior",
        "PageDown" => "Next",
        "Space" => "space",
        _ when Key.Length == 1 => Key.ToLowerInvariant(),
        _ => Key,
    };

    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint mods = 0;
        string? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Ctrl; break;
                case "alt": mods |= Alt; break;
                case "shift": mods |= Shift; break;
                case "super" or "win" or "meta": mods |= Super; break;
                default:
                    key = NormalizeKey(part);
                    if (key is null) return null;
                    break;
            }
        }
        // Needs a modifier other than Shift, so plain typing never triggers it.
        return key is not null && (mods & ~Shift) != 0 ? new Hotkey(mods, key) : null;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & Ctrl) != 0) parts.Add("Ctrl");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Shift");
        if ((Modifiers & Super) != 0) parts.Add("Super");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}
