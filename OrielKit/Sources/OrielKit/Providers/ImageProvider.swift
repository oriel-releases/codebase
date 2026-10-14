import Foundation

public struct ImageQuery: Sendable, Hashable {
    public var query: String
    public var targetSize: PixelSize

    public init(_ query: String, targetSize: PixelSize) {
        self.query = query
        self.targetSize = targetSize
    }
}

/// One page of search results, most relevant first.
public struct SearchPage: Sendable {
    public var photos: [PhotoCandidate]
    public var totalPages: Int

    public init(photos: [PhotoCandidate], totalPages: Int) {
        self.photos = photos
        self.totalPages = totalPages
    }
}

/// A source of photos. Unsplash today; Pexels / Bing / Wikimedia could be added later.
public protocol ImageProvider: Sendable {
    var name: String { get }

    /// True when the provider has what it needs (an API key).
    var isConfigured: Bool { get }

    /// Requests left in the current hourly window, if the provider has told us yet.
    var remainingRequests: Int? { get async }

    /// Good photos for the query, in a random order.
    func random(_ query: ImageQuery) async throws -> [PhotoCandidate]

    /// Photos matching the words, most relevant first. Pages start at 1.
    func search(_ query: String, page: Int) async throws -> SearchPage

    /// One photo by its provider ID.
    func photo(id: String) async throws -> PhotoCandidate

    /// Downloads the photo sized for `size` as JPEG bytes.
    func download(_ photo: PhotoCandidate, size: PixelSize) async throws -> Data

    /// Tells the provider the photo was used (required by Unsplash's guidelines). Never throws.
    func trackUsage(_ photo: PhotoCandidate) async
}

public struct ProviderError: Error, LocalizedError, Sendable, Equatable {
    public enum Kind: Sendable, Equatable {
        case missingKey, unauthorized, rateLimited, noResults, network, badResponse
    }

    public var kind: Kind
    public var message: String

    public init(_ kind: Kind, _ message: String) {
        self.kind = kind
        self.message = message
    }

    public var errorDescription: String? { message }

    /// Errors worth retrying later with backoff.
    public var isTransient: Bool { kind == .network || kind == .rateLimited || kind == .badResponse }
}

/// The HTTP calls Oriel makes, so tests can answer them without a network. URLSession is the real one.
public protocol HTTPClient: Sendable {
    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse)
}

extension URLSession: HTTPClient {
    public func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let (data, response) = try await data(for: request)
        guard let http = response as? HTTPURLResponse else { throw URLError(.badServerResponse) }
        return (data, http)
    }
}

public extension URLSession {
    /// The session Oriel uses: identifies itself, no HTTP cache (photos are saved to disk anyway).
    static let oriel: URLSession = {
        let config = URLSessionConfiguration.default
        config.httpAdditionalHeaders = ["User-Agent": "Oriel/1.0 (+macOS desktop wallpaper app)"]
        config.timeoutIntervalForRequest = 60
        config.urlCache = nil
        return URLSession(configuration: config)
    }()
}
