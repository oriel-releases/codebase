import Foundation
import Testing
@testable import OrielKit

struct UnsplashProviderTests {
    static let onePhoto = """
        {"total": 1, "total_pages": 1, "results": [{
          "id": "abc123", "width": 6000, "height": 4000, "likes": 321, "color": "#0c2626",
          "description": null, "alt_description": "a mountain at dawn",
          "urls": { "raw": "https://images.unsplash.com/photo-1?ixid=foo", "full": "x", "regular": "x", "small": "https://s", "thumb": "https://t" },
          "links": { "html": "https://unsplash.com/photos/abc123", "download_location": "https://api.unsplash.com/photos/abc123/download?ixid=foo" },
          "user": { "name": "Jane Doe", "username": "jane", "links": { "html": "https://unsplash.com/@jane" } }
        }]}
        """

    static let query = ImageQuery("night sky", targetSize: PixelSize(width: 2560, height: 1440))

    @Test func parsesPhotosAndSendsAuth() async throws {
        let http = StubHTTP { StubHTTP.json(Self.onePhoto, for: $0) }
        let provider = UnsplashProvider(http: http, keys: FakeKeyStore(key: "secret"))

        let photos = try await provider.random(Self.query)

        let photo = try #require(photos.first)
        #expect(photos.count == 1)
        #expect(photo.id == "abc123")
        #expect(photo.width == 6000)
        #expect(photo.likes == 321)
        #expect(photo.color == "#0c2626")
        #expect(photo.photographerName == "Jane Doe")
        #expect(photo.description == "a mountain at dawn")
        #expect(photo.thumbnailURL.absoluteString == "https://s")
        #expect(photo.photographerURL.absoluteString == "https://unsplash.com/@jane?utm_source=oriel&utm_medium=referral")

        let request = try #require(http.requests.first)
        #expect(request.value(forHTTPHeaderField: "Authorization") == "Client-ID secret")
        #expect(request.value(forHTTPHeaderField: "Accept-Version") == "v1")
        let query = try #require(request.url?.query(percentEncoded: true))
        #expect(query.contains("orientation=landscape"))
        #expect(query.contains("query=night%20sky"))
        #expect(await provider.remainingRequests == 49)
    }

    @Test func missingKeyFailsWithoutCallingTheAPI() async {
        let http = StubHTTP { StubHTTP.json(Self.onePhoto, for: $0) }
        let provider = UnsplashProvider(http: http, keys: FakeKeyStore(key: nil))

        await #expect(throws: ProviderError(.missingKey, "This build of Oriel has no Unsplash access key.")) {
            try await provider.random(Self.query)
        }
        #expect(http.requests.isEmpty)
    }

    @Test(arguments: [
        (401, 10, ProviderError.Kind.unauthorized),
        (403, 0, .rateLimited),
        (403, 10, .unauthorized),
        (429, 10, .rateLimited),
        (404, 10, .noResults),
        (500, 10, .badResponse),
    ])
    func mapsHTTPErrors(status: Int, remaining: Int, expected: ProviderError.Kind) async {
        let http = StubHTTP { StubHTTP.json("{}", status: status, remaining: remaining, for: $0) }
        let provider = UnsplashProvider(http: http, keys: FakeKeyStore(key: "k"))

        do {
            _ = try await provider.random(Self.query)
            Issue.record("Expected an error")
        } catch let error as ProviderError {
            #expect(error.kind == expected)
        } catch {
            Issue.record("Unexpected \(error)")
        }
    }

    @Test func stopsCallingTheAPIOnceRateLimited() async throws {
        let http = StubHTTP { StubHTTP.json(Self.onePhoto, remaining: 0, for: $0) }
        let provider = UnsplashProvider(http: http, keys: FakeKeyStore(key: "k"))

        _ = try await provider.random(Self.query) // succeeds, but that was the last request this hour

        await #expect(throws: ProviderError.self) { try await provider.random(Self.query) }
        #expect(http.requests.count == 1)
    }

    @Test func offlineIsATransientNetworkError() async {
        let http = StubHTTP { _ in throw URLError(.notConnectedToInternet) }
        let provider = UnsplashProvider(http: http, keys: FakeKeyStore(key: "k"))

        do {
            _ = try await provider.random(Self.query)
            Issue.record("Expected an error")
        } catch let error as ProviderError {
            #expect(error.kind == .network)
            #expect(error.isTransient)
        } catch {
            Issue.record("Unexpected \(error)")
        }
    }

    @Test func unreadableJSONIsABadResponse() async {
        let http = StubHTTP { StubHTTP.json("{\"results\": [{\"id\": 1}]}", for: $0) }
        let provider = UnsplashProvider(http: http, keys: FakeKeyStore(key: "k"))

        await #expect(throws: ProviderError(.badResponse, "Unsplash returned data Oriel couldn't read.")) {
            try await provider.search("x", page: 1)
        }
    }

    @Test func downloadURLAsksForTheExactSizeAndQuality() {
        let url = UnsplashProvider.downloadURL(URL(string: "https://images.unsplash.com/photo-1?ixid=foo")!,
                                               size: PixelSize(width: 3840, height: 2160))
        #expect(url.absoluteString == "https://images.unsplash.com/photo-1?ixid=foo&w=3840&h=2160&fit=crop&crop=entropy&q=90&fm=jpg")
    }

    @Test func downloadRetriesOnceThenGivesUp() async {
        let http = StubHTTP { _ in throw URLError(.timedOut) }
        let provider = UnsplashProvider(http: http, keys: FakeKeyStore(key: "k"))

        await #expect(throws: ProviderError.self) {
            try await provider.download(Fixtures.photo("a"), size: PixelSize(width: 10, height: 10))
        }
        #expect(http.requests.count == 2)
    }

    @Test func trackingHitsTheDownloadLocation() async {
        let http = StubHTTP { StubHTTP.json("{\"url\":\"x\"}", for: $0) }
        let provider = UnsplashProvider(http: http, keys: FakeKeyStore(key: "k"))

        await provider.trackUsage(Fixtures.photo("abc"))

        #expect(http.requests.map { $0.url?.absoluteString } == ["https://api.unsplash.com/photos/abc/download"])
    }
}
