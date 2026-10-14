import Foundation

/// Photos from the Unsplash API. Every install shares one access key and so one hourly limit: the provider
/// remembers what the API says is left and stops early instead of hammering it.
public actor UnsplashProvider: ImageProvider {
    public static let apiBase = URL(string: "https://api.unsplash.com/")!
    static let utm = "utm_source=oriel&utm_medium=referral"
    /// How long one try at downloading a photo may take; a slow connection gets a second try.
    static let downloadTimeout: TimeInterval = 25

    private let http: HTTPClient
    private let keys: ApiKeyStore
    private var remaining: Int?
    private var rateLimitedUntil = Date.distantPast

    public init(http: HTTPClient = URLSession.oriel, keys: ApiKeyStore = BuiltInApiKeyStore()) {
        self.http = http
        self.keys = keys
    }

    public nonisolated var name: String { "unsplash" }
    public nonisolated var isConfigured: Bool { keys.hasKey }
    public var remainingRequests: Int? { remaining }

    public func random(_ query: ImageQuery) async throws -> [PhotoCandidate] {
        // Relevance first: pick from the best matches (a random one of the first few pages, so days differ)
        // instead of Unsplash's loosely related random endpoint.
        let page = Int.random(in: 1...3)
        var found = try await search(query.query, page: page)
        if found.photos.isEmpty && page > 1 { found = try await search(query.query, page: 1) }
        guard !found.photos.isEmpty else { throw ProviderError(.noResults, "No photos found for that topic.") }
        return found.photos.shuffled()
    }

    public func search(_ query: String, page: Int) async throws -> SearchPage {
        var url = Self.apiBase.appending(path: "search/photos")
        url.append(queryItems: [
            URLQueryItem(name: "orientation", value: "landscape"),
            URLQueryItem(name: "content_filter", value: "high"),
            URLQueryItem(name: "order_by", value: "relevant"),
            URLQueryItem(name: "per_page", value: "30"),
            URLQueryItem(name: "page", value: String(max(1, page))),
            URLQueryItem(name: "query", value: query),
        ])
        let data = try await sendAPI(url)
        do {
            let result = try JSONDecoder().decode(SearchResponse.self, from: data)
            return SearchPage(photos: try result.results.map(Self.parse), totalPages: result.totalPages ?? 1)
        } catch {
            throw ProviderError(.badResponse, "Unsplash returned data Oriel couldn't read.")
        }
    }

    public func photo(id: String) async throws -> PhotoCandidate {
        let data = try await sendAPI(Self.apiBase.appending(path: "photos").appending(path: id))
        do {
            return try Self.parse(JSONDecoder().decode(APIPhoto.self, from: data))
        } catch {
            throw ProviderError(.badResponse, "Unsplash returned data Oriel couldn't read.")
        }
    }

    public func download(_ photo: PhotoCandidate, size: PixelSize) async throws -> Data {
        // The image CDN (images.unsplash.com) needs no key and doesn't count against the API limit.
        var request = URLRequest(url: Self.downloadURL(photo.rawURL, size: size))
        request.timeoutInterval = Self.downloadTimeout
        var attempt = 1
        while true {
            do {
                let (data, response) = try await http.send(request)
                guard (200..<300).contains(response.statusCode) else { throw URLError(.badServerResponse) }
                return data
            } catch {
                try Task.checkCancellation()
                if attempt < 2 {
                    Log.provider.info("Downloading \(photo.id) failed (\(error.localizedDescription)); trying once more")
                    attempt += 1
                    continue
                }
                throw ProviderError(.network, (error as? URLError)?.code == .timedOut
                    ? "The photo is taking too long to download. Check your connection and try again."
                    : "Couldn't download the photo. Check your connection and try again.")
            }
        }
    }

    public func trackUsage(_ photo: PhotoCandidate) async {
        guard let url = photo.downloadTrackingURL else { return }
        do {
            _ = try await sendAPI(url)
        } catch {
            Log.provider.warning("Download tracking failed for \(photo.id): \(error.localizedDescription)")
        }
    }

    static func downloadURL(_ rawURL: URL, size: PixelSize) -> URL {
        var components = URLComponents(url: rawURL, resolvingAgainstBaseURL: false)!
        components.queryItems = (components.queryItems ?? []) + [
            URLQueryItem(name: "w", value: String(size.width)),
            URLQueryItem(name: "h", value: String(size.height)),
            URLQueryItem(name: "fit", value: "crop"),
            URLQueryItem(name: "crop", value: "entropy"),
            URLQueryItem(name: "q", value: "90"),
            URLQueryItem(name: "fm", value: "jpg"),
        ]
        return components.url!
    }

    private func sendAPI(_ url: URL) async throws -> Data {
        guard let key = keys.key else {
            throw ProviderError(.missingKey, "This build of Oriel has no Unsplash access key.")
        }
        if Date() < rateLimitedUntil { throw Self.rateLimitedError }

        var request = URLRequest(url: url)
        request.setValue("Client-ID \(key)", forHTTPHeaderField: "Authorization")
        request.setValue("v1", forHTTPHeaderField: "Accept-Version")

        let data: Data
        let response: HTTPURLResponse
        do {
            (data, response) = try await http.send(request)
        } catch {
            try Task.checkCancellation()
            throw ProviderError(.network, (error as? URLError)?.code == .timedOut
                ? "Unsplash took too long to answer."
                : "Can't reach Unsplash. Are you offline?")
        }

        readRateLimit(response)
        switch response.statusCode {
        case 200..<300: return data
        case 401: throw ProviderError(.unauthorized, "Unsplash rejected Oriel's access key.")
        case 403 where remaining == 0, 429: throw rateLimited()
        case 403: throw ProviderError(.unauthorized, "Unsplash refused the request (403).")
        case 404: throw ProviderError(.noResults, "That photo isn't available (any more).")
        default: throw ProviderError(.badResponse, "Unsplash returned HTTP \(response.statusCode).")
        }
    }

    private static let rateLimitedError = ProviderError(.rateLimited, "Unsplash hourly limit reached. Will try again later.")

    /// Limits reset on the hour.
    private func rateLimited() -> ProviderError {
        let now = Date()
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = .gmt
        let hour = utc.dateInterval(of: .hour, for: now)?.start ?? now
        rateLimitedUntil = hour.addingTimeInterval(3600)
        return Self.rateLimitedError
    }

    private func readRateLimit(_ response: HTTPURLResponse) {
        guard let value = response.value(forHTTPHeaderField: "X-Ratelimit-Remaining"), let left = Int(value) else { return }
        remaining = left
        if left <= 0 { _ = rateLimited() }
    }

    // MARK: - JSON

    struct SearchResponse: Decodable {
        let results: [APIPhoto]
        let totalPages: Int?

        enum CodingKeys: String, CodingKey {
            case results
            case totalPages = "total_pages"
        }
    }

    struct APIPhoto: Decodable {
        struct URLs: Decodable { let raw: String; let small: String?; let thumb: String? }
        struct Links: Decodable {
            let html: String
            let downloadLocation: String?
            enum CodingKeys: String, CodingKey { case html, downloadLocation = "download_location" }
        }
        struct User: Decodable {
            struct UserLinks: Decodable { let html: String }
            let name: String?
            let username: String?
            let links: UserLinks
        }

        let id: String
        let width: Int
        let height: Int
        let likes: Int?
        let color: String?
        let description: String?
        let altDescription: String?
        let urls: URLs
        let links: Links
        let user: User

        enum CodingKeys: String, CodingKey {
            case id, width, height, likes, color, description, urls, links, user
            case altDescription = "alt_description"
        }
    }

    struct ParseError: Error {}

    static func parse(_ p: APIPhoto) throws -> PhotoCandidate {
        guard let raw = URL(string: p.urls.raw),
              let thumb = URL(string: p.urls.small ?? p.urls.thumb ?? p.urls.raw),
              let page = URL(string: addUTM(p.links.html)),
              let photographer = URL(string: addUTM(p.user.links.html)) else { throw ParseError() }
        return PhotoCandidate(
            provider: "unsplash",
            id: p.id,
            width: p.width,
            height: p.height,
            likes: p.likes ?? 0,
            description: joinNonEmpty(p.altDescription, p.description),
            color: p.color,
            rawURL: raw,
            thumbnailURL: thumb,
            photoPageURL: page,
            photographerName: [p.user.name, p.user.username].compactMap { $0 }.first { !$0.isEmpty } ?? "Unknown",
            photographerURL: photographer,
            downloadTrackingURL: p.links.downloadLocation.flatMap(URL.init(string:)))
    }

    static func joinNonEmpty(_ parts: String?...) -> String? {
        var kept: [String] = []
        for case let part? in parts {
            let trimmed = part.trimmingCharacters(in: .whitespacesAndNewlines)
            if !trimmed.isEmpty && !kept.contains(where: { $0.caseInsensitiveCompare(trimmed) == .orderedSame }) {
                kept.append(trimmed)
            }
        }
        return kept.isEmpty ? nil : kept.joined(separator: " — ")
    }

    static func addUTM(_ url: String) -> String { url + (url.contains("?") ? "&" : "?") + utm }
}
