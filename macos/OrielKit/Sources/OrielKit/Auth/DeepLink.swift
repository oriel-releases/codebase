import Foundation

/// The `oriel://` links the website and the browser extension open:
/// - `oriel://auth?token=` (a Google ID token) or `?refresh=` (an email account's Firebase session), maybe with
///   `&share={id}&from={Name}` and/or `&follow={CODE}` carried through sign-in;
/// - `oriel://wallpaper?id={photoId}&from={Name}` (a shared photo);
/// - `oriel://follow?code={CODE}` (a friend's follow page);
/// - `oriel://open` (just bring Oriel to the front).
public enum DeepLink {
    public static func googleToken(_ links: [String]) -> String? { authValue(links, "token") }

    /// An email and password sign-in link (the website's Firebase refresh token).
    public static func sessionToken(_ links: [String]) -> String? { authValue(links, "refresh") }

    private static func authValue(_ links: [String], _ name: String) -> String? {
        for case let (host, query)? in links.map(parse) where host == "auth" {
            if let value = query[name], !value.trimmingCharacters(in: .whitespaces).isEmpty { return value }
        }
        return nil
    }

    /// A shared photo to set as the wallpaper (only a plain Unsplash photo ID is accepted), and the sender's first name.
    public static func sharedPhoto(_ links: [String]) -> (id: String, from: String)? {
        for case let (host, query)? in links.map(parse) where host == "wallpaper" {
            if let id = query["id"], isPhotoId(id) { return (id, ShareLink.cleanName(query["from"])) }
        }
        return nil
    }

    /// A shared photo carried through sign-in: someone opened a share link, installed Oriel and signed in.
    public static func pendingShare(_ links: [String]) -> (id: String, from: String)? {
        for case let (host, query)? in links.map(parse) where host == "auth" {
            if let id = query["share"], isPhotoId(id) { return (id, ShareLink.cleanName(query["from"])) }
        }
        return nil
    }

    /// A follow code from a friend's follow page, or carried through sign-in.
    public static func followCode(_ links: [String]) -> String? {
        for case let (host, query)? in links.map(parse) {
            let value = host == "follow" ? query["code"] : host == "auth" ? query["follow"] : nil
            if let code = FollowCodes.normalize(value) { return code }
        }
        return nil
    }

    /// True for `oriel://open`, which just brings Oriel to the front.
    public static func isOpen(_ links: [String]) -> Bool {
        links.contains { parse($0)?.host == "open" }
    }

    /// True if any argument is an oriel:// link, whether or not it carries something usable.
    public static func isLink(_ links: [String]) -> Bool {
        links.contains { clean($0).lowercased().hasPrefix("oriel://") }
    }

    static func isPhotoId(_ id: String) -> Bool {
        (6...24).contains(id.count) && id.allSatisfy { ($0.isASCII && ($0.isLetter || $0.isNumber)) || $0 == "_" || $0 == "-" }
    }

    private static func clean(_ s: String) -> String {
        s.trimmingCharacters(in: .whitespacesAndNewlines).trimmingCharacters(in: CharacterSet(charactersIn: "\""))
    }

    /// The command (host, lower case) and the query values, for an oriel:// link; nil for anything else.
    private static func parse(_ link: String) -> (host: String, query: [String: String])? {
        guard let components = URLComponents(string: clean(link)), components.scheme?.lowercased() == "oriel",
              let host = components.host?.lowercased() else { return nil }
        var query: [String: String] = [:]
        for item in components.queryItems ?? [] where query[item.name] == nil {
            query[item.name] = item.value ?? ""
        }
        return (host, query)
    }
}

/// Follow codes: 8 characters friends read aloud and type by hand.
public enum FollowCodes {
    /// No 0/O, 1/I.
    static let alphabet = Array("ABCDEFGHJKLMNPQRSTUVWXYZ23456789")
    public static let length = 8

    public static func new() -> String {
        var generator = SystemRandomNumberGenerator()
        return String((0..<length).map { _ in alphabet.randomElement(using: &generator)! })
    }

    /// "k7mq-2xpa " becomes "K7MQ2XPA"; nil when it can't be a code.
    public static func normalize(_ input: String?) -> String? {
        guard let input else { return nil }
        let code = String(input.uppercased().filter { !$0.isWhitespace && $0 != "-" })
        return code.count == length && code.allSatisfy(alphabet.contains) ? code : nil
    }

    /// "K7MQ2XPA" shown as "K7MQ-2XPA".
    public static func format(_ code: String) -> String {
        code.count == length ? "\(code.prefix(4))-\(code.dropFirst(4))" : code
    }

    /// What someone is typing, tidied as they type: capitals, only letters and numbers, at most a code long, and the
    /// dash after the fourth ("k7mq2x" becomes "K7MQ-2X").
    public static func formatInput(_ input: String?) -> String {
        let chars = String((input ?? "").uppercased().filter { $0.isASCII && ($0.isLetter || $0.isNumber) }.prefix(length))
        return chars.count > 4 ? "\(chars.prefix(4))-\(chars.dropFirst(4))" : chars
    }
}
