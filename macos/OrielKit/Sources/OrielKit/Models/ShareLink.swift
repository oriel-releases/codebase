import Foundation

/// An Unsplash photo as the website's share and follow pages show it: its ID, image path and photographer.
public struct SharedPhoto: Sendable, Hashable {
    public var id: String
    public var path: String
    public var photographer: String
    public var handle: String

    public init(id: String, path: String, photographer: String, handle: String) {
        self.id = id
        self.path = path
        self.photographer = photographer
        self.handle = handle
    }
}

/// The links a user sends a friend. They only carry an ID (`/share/AB3K9QXZ`, `/follow/K7MQ2XPA`); the page reads the
/// rest from Firestore once the friend has signed in on the website.
public enum ShareLink {
    public static func forShare(_ id: String) -> String { "\(OrielLinks.share)/\(id)" }
    public static func forFollow(_ code: String) -> String { "\(OrielLinks.follow)/\(code)" }

    /// The photo as a page shows it. Nil for photos that can't be shared (only Unsplash photos have a public image).
    public static func photo(_ entry: HistoryEntry) -> SharedPhoto? {
        guard let image = URL(string: entry.thumbnailURL), image.scheme != nil, image.host() == "images.unsplash.com" else { return nil }
        let path = String(image.path(percentEncoded: true).drop { $0 == "/" })
        guard (6...120).contains(path.count), path.allSatisfy({ isASCIIAlnum($0) || $0 == "_" || $0 == "-" }) else { return nil }
        // The photographer's Unsplash handle, so the page can link their name to their profile.
        var handle = ""
        if let profile = URL(string: entry.photographerURL), profile.host() == "unsplash.com" {
            let p = String(profile.path(percentEncoded: true).drop { $0 == "/" })
            if p.first == "@" {
                let h = p.dropFirst()
                if (1...40).contains(h.count), h.allSatisfy({ isASCIIAlnum($0) || $0 == "_" || $0 == "-" || $0 == "." }) {
                    handle = String(h)
                }
            }
        }
        return SharedPhoto(id: entry.photoID, path: path, photographer: String(entry.photographerName.prefix(80)), handle: handle)
    }

    /// A Google profile picture to show on those pages, or "" (any other address is never stored).
    public static func picture(_ url: String?) -> String {
        guard let url, url.count <= 400, let u = URL(string: url), u.scheme == "https", u.host() == "lh3.googleusercontent.com" else {
            return ""
        }
        return url
    }

    /// A first name that is safe to put in a link and show as text: no control characters or angle brackets, at most
    /// 30 characters.
    public static func cleanName(_ name: String?) -> String {
        guard let name, let first = name.trimmingCharacters(in: .whitespacesAndNewlines).split(separator: " ").first else { return "" }
        let clean = first.unicodeScalars.filter { !CharacterSet.controlCharacters.contains($0) && $0 != "<" && $0 != ">" }
        return String(String(String.UnicodeScalarView(clean)).prefix(30))
    }

    static func isASCIIAlnum(_ c: Character) -> Bool {
        c.isASCII && (c.isLetter || c.isNumber)
    }
}
