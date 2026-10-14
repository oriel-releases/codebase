import Foundation

/// The current time, replaceable in tests.
public protocol OrielClock: Sendable {
    var now: Date { get }
    var calendar: Calendar { get }
}

public extension OrielClock {
    var calendar: Calendar { .current }
    /// Today on this Mac.
    var today: LocalDate { LocalDate(now, calendar: calendar) }
}

public struct SystemClock: OrielClock {
    public init() {}
    public var now: Date { Date() }
}

/// Waits after failed changes: 2m → 5m → 15m → 30m → 1h, then hourly.
public enum RetryBackoff {
    static let steps: [TimeInterval] = [2 * 60, 5 * 60, 15 * 60, 30 * 60, 60 * 60]

    public static func delay(afterFailures consecutiveFailures: Int) -> TimeInterval {
        steps[min(max(consecutiveFailures - 1, 0), steps.count - 1)]
    }
}

public enum CandidateFilter {
    /// Why a candidate was rejected, or nil if it's fine.
    public static func reject(_ c: PhotoCandidate, target: PixelSize, settings s: OrielSettings) -> String? {
        if !c.isLandscape { return "portrait" }
        if c.width < target.width || c.height < target.height { return "too small" }
        if s.skipLowLikes && c.likes < s.minLikes { return "low likes" }
        return nil
    }
}

public protocol TopicPicker: Sendable {
    /// A random enabled topic, avoiding `avoid` when there's a choice.
    func pick(_ topics: [String], avoiding avoid: Set<String>) -> String
}

public struct RandomTopicPicker: TopicPicker {
    /// What to search when no topic is enabled.
    public static let fallback = "landscape"

    public init() {}

    public func pick(_ topics: [String], avoiding avoid: Set<String> = []) -> String {
        guard !topics.isEmpty else { return Self.fallback }
        let pool = topics.filter { !avoid.contains($0) }
        return (pool.isEmpty ? topics : pool).randomElement()!
    }
}

/// Which kind of photo suits the moment.
public enum Mood: Sendable {
    case any, dark, bright
}

/// Darker, calmer photos in the evening and at night (and whenever the Mac is in dark mode), brighter ones in the
/// morning. Only an order of preference: when no photo fits, any good photo is still used.
public enum PhotoMood {
    public static func mood(at date: Date, calendar: Calendar = .current, systemDark: Bool) -> Mood {
        let h = calendar.component(.hour, from: date)
        if h >= 19 || h < 6 || systemDark { return .dark }
        return h < 11 ? .bright : .any
    }

    /// Relative luminance (0 black to 1 white) of a "#RRGGBB" color, or nil.
    public static func luminance(_ hex: String?) -> Double? {
        guard let hex, hex.count == 7, hex.first == "#", let rgb = Int(hex.dropFirst(), radix: 16) else { return nil }
        func linear(_ c: Int) -> Double {
            let v = Double(c) / 255
            return v <= 0.03928 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4)
        }
        return 0.2126 * linear(rgb >> 16 & 0xFF) + 0.7152 * linear(rgb >> 8 & 0xFF) + 0.0722 * linear(rgb & 0xFF)
    }

    /// True when the photo suits the mood. A photo without a known color always does.
    public static func fits(_ photo: PhotoCandidate, _ mood: Mood) -> Bool {
        guard let l = luminance(photo.color) else { return true }
        switch mood {
        case .dark: return l < 0.18
        case .bright: return l > 0.3
        case .any: return true
        }
    }

    /// The photos that suit the mood first, in their original order, then the rest.
    public static func order(_ photos: [PhotoCandidate], _ mood: Mood) -> [PhotoCandidate] {
        guard mood != .any else { return photos }
        return photos.filter { fits($0, mood) } + photos.filter { !fits($0, mood) }
    }
}
