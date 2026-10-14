import Foundation

/// A photo offered by an image provider, before it is downloaded.
public struct PhotoCandidate: Sendable, Hashable {
    public var provider: String
    public var id: String
    public var width: Int
    public var height: Int
    public var likes: Int
    public var description: String?
    /// The photo's main color as "#RRGGBB" (Unsplash's `color`), used for time-of-day picks.
    public var color: String?

    /// Base URL that accepts sizing params (Unsplash "raw").
    public var rawURL: URL
    public var thumbnailURL: URL
    public var photoPageURL: URL
    public var photographerName: String
    public var photographerURL: URL

    /// Provider endpoint that must be pinged when the photo is used (Unsplash download tracking).
    public var downloadTrackingURL: URL?

    public init(provider: String, id: String, width: Int, height: Int, likes: Int = 0, description: String? = nil,
                color: String? = nil, rawURL: URL, thumbnailURL: URL, photoPageURL: URL, photographerName: String,
                photographerURL: URL, downloadTrackingURL: URL? = nil) {
        self.provider = provider
        self.id = id
        self.width = width
        self.height = height
        self.likes = likes
        self.description = description
        self.color = color
        self.rawURL = rawURL
        self.thumbnailURL = thumbnailURL
        self.photoPageURL = photoPageURL
        self.photographerName = photographerName
        self.photographerURL = photographerURL
        self.downloadTrackingURL = downloadTrackingURL
    }

    public var isLandscape: Bool { width > height }
}

/// How a photo fills the screen. Raw values match the Windows app's settings files (Tile isn't offered on macOS).
public enum WallpaperFit: String, Sendable, CaseIterable, Codable {
    case fill = "Fill"
    case fit = "Fit"
    case stretch = "Stretch"
    case center = "Center"
    /// One photo across all screens, each showing its part.
    case span = "Span"

    public init(from decoder: Decoder) throws {
        // Unknown values (like Windows' "Tile") fall back to Fill instead of failing the whole settings file.
        self = WallpaperFit(rawValue: try decoder.singleValueContainer().decode(String.self)) ?? .fill
    }
}

/// A screen, in physical pixels. `frame` is in the global desktop space (main screen's top-left at 0,0).
public struct ScreenInfo: Sendable, Hashable, Identifiable {
    /// Stable across restarts and reconnects (the display's UUID).
    public var id: String
    public var frame: PixelRect
    public var isMain: Bool

    public init(id: String, frame: PixelRect, isMain: Bool) {
        self.id = id
        self.frame = frame
        self.isMain = isMain
    }

    public var size: PixelSize { PixelSize(width: frame.width, height: frame.height) }
}

public struct PixelSize: Sendable, Hashable, Codable {
    public var width: Int
    public var height: Int

    public init(width: Int, height: Int) {
        self.width = width
        self.height = height
    }
}

public struct PixelRect: Sendable, Hashable, Codable {
    public var x: Int
    public var y: Int
    public var width: Int
    public var height: Int

    public init(x: Int, y: Int, width: Int, height: Int) {
        self.x = x
        self.y = y
        self.width = width
        self.height = height
    }
}

/// A wallpaper that Oriel has applied at some point.
public struct HistoryEntry: Sendable, Hashable, Identifiable {
    public var id: Int64
    public var provider: String
    public var photoID: String
    /// Where the photo is saved (a plain path, as stored in the database).
    public var filePath: String
    public var thumbnailURL: String
    public var photoPageURL: String
    public var photographerName: String
    public var photographerURL: String
    public var description: String?
    public var topic: String?
    /// The screen it was set on when each screen has its own photo; nil when it was on all of them.
    public var screenID: String?
    public var appliedAt: Date
    public var isFavorite: Bool
    public var isDeleted: Bool

    public init(id: Int64 = 0, provider: String, photoID: String, filePath: String, thumbnailURL: String,
                photoPageURL: String, photographerName: String, photographerURL: String, description: String? = nil,
                topic: String? = nil, screenID: String? = nil, appliedAt: Date, isFavorite: Bool = false,
                isDeleted: Bool = false) {
        self.id = id
        self.provider = provider
        self.photoID = photoID
        self.filePath = filePath
        self.thumbnailURL = thumbnailURL
        self.photoPageURL = photoPageURL
        self.photographerName = photographerName
        self.photographerURL = photographerURL
        self.description = description
        self.topic = topic
        self.screenID = screenID
        self.appliedAt = appliedAt
        self.isFavorite = isFavorite
        self.isDeleted = isDeleted
    }

    public var fileURL: URL { URL(filePath: filePath) }
}

/// A calendar day with no time or zone ("2026-10-14"), like .NET's DateOnly.
public struct LocalDate: Sendable, Hashable, Comparable, Codable, CustomStringConvertible {
    public var year: Int
    public var month: Int
    public var day: Int

    public init(year: Int, month: Int, day: Int) {
        self.year = year
        self.month = month
        self.day = day
    }

    /// The day `date` falls on in `calendar`'s time zone.
    public init(_ date: Date, calendar: Calendar = .current) {
        let c = calendar.dateComponents([.year, .month, .day], from: date)
        self.init(year: c.year!, month: c.month!, day: c.day!)
    }

    public init?(string: String) {
        let parts = string.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3 else { return nil }
        self.init(year: parts[0], month: parts[1], day: parts[2])
    }

    public var description: String { String(format: "%04d-%02d-%02d", year, month, day) }

    public static func < (a: LocalDate, b: LocalDate) -> Bool { (a.year, a.month, a.day) < (b.year, b.month, b.day) }

    public init(from decoder: Decoder) throws {
        let text = try decoder.singleValueContainer().decode(String.self)
        guard let date = LocalDate(string: text) else {
            throw DecodingError.dataCorrupted(.init(codingPath: decoder.codingPath, debugDescription: "Not a date: \(text)"))
        }
        self = date
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.singleValueContainer()
        try c.encode(description)
    }
}
