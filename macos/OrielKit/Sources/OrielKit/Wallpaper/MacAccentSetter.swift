import Foundation

/// Changes the system accent color to match the wallpaper ("Match accent color", off by default).
public protocol AccentSetting: Sendable {
    /// Best effort: the closest accent the system offers to "#RRGGBB".
    func apply(_ hex: String)
}

/// macOS offers 8 accent colors (System Settings > Appearance), not any color like Windows: Oriel picks the one closest
/// to the wallpaper's. It writes the same global preferences System Settings writes (the accent and the matching text
/// highlight), then sends the "colors changed" notification, so the Dock, Finder, menus and most apps switch at once.
/// These keys aren't a public API, but they've been the same for many macOS versions.
public struct MacAccentSetter: AccentSetting {
    public struct Preset: Sendable, Equatable {
        public var name: String
        /// `AppleAccentColor`.
        public var value: Int
        /// The accent's hue in degrees (nil for Graphite).
        var hue: Double?
        /// `AppleHighlightColor`: the text selection color that goes with it.
        var highlight: String
    }

    public static let presets = [
        Preset(name: "Red", value: 0, hue: 1, highlight: "1.000000 0.733333 0.721569 Red"),
        Preset(name: "Orange", value: 1, hue: 28, highlight: "1.000000 0.874510 0.701961 Orange"),
        Preset(name: "Yellow", value: 2, hue: 47, highlight: "1.000000 0.937255 0.690196 Yellow"),
        Preset(name: "Green", value: 3, hue: 105, highlight: "0.752941 0.964706 0.678431 Green"),
        Preset(name: "Blue", value: 4, hue: 211, highlight: "0.698039 0.843137 1.000000 Blue"),
        Preset(name: "Purple", value: 5, hue: 299, highlight: "0.968627 0.831373 1.000000 Purple"),
        Preset(name: "Pink", value: 6, hue: 332, highlight: "1.000000 0.749020 0.823529 Pink"),
        Preset(name: "Graphite", value: -1, hue: nil, highlight: "0.847059 0.847059 0.862745 Graphite"),
    ]

    public init() {}

    /// The system accent closest to "#RRGGBB": by hue, or Graphite for a nearly gray color.
    public static func nearest(_ hex: String) -> Preset? {
        guard hex.count == 7, hex.first == "#", let rgb = Int(hex.dropFirst(), radix: 16) else { return nil }
        let (h, s, _) = AccentPicker.toHSV(Double(rgb >> 16 & 0xFF), Double(rgb >> 8 & 0xFF), Double(rgb & 0xFF))
        if s < 0.18 { return presets.last }
        return presets.filter { $0.hue != nil }.min { distance($0.hue!, h) < distance($1.hue!, h) }
    }

    static func distance(_ a: Double, _ b: Double) -> Double {
        let d = abs(a - b).truncatingRemainder(dividingBy: 360)
        return min(d, 360 - d)
    }

    public func apply(_ hex: String) {
        guard let preset = Self.nearest(hex) else { return }
        let any = kCFPreferencesAnyApplication, user = kCFPreferencesCurrentUser, host = kCFPreferencesAnyHost
        let current = CFPreferencesCopyValue("AppleAccentColor" as CFString, any, user, host) as? Int
        guard current != preset.value else { return }
        CFPreferencesSetValue("AppleAccentColor" as CFString, preset.value as CFNumber, any, user, host)
        CFPreferencesSetValue("AppleHighlightColor" as CFString, preset.highlight as CFString, any, user, host)
        CFPreferencesSynchronize(any, user, host)
        // Running apps pick the new colors up from these.
        let center = DistributedNotificationCenter.default()
        center.postNotificationName(Notification.Name("AppleColorPreferencesChangedNotification"), object: nil, userInfo: nil,
                                    deliverImmediately: true)
        center.postNotificationName(Notification.Name("AppleAquaColorVariantChanged"), object: nil, userInfo: nil,
                                    deliverImmediately: true)
        Log.wallpaper.info("Accent color set to \(preset.name) for \(hex)")
    }
}
