import Foundation
import Testing
@testable import OrielKit

/// Talks to the real Unsplash API (one API request; the image download doesn't count). Off by default:
///   ORIEL_LIVE_KEY=<access key> scripts/test.sh --filter LiveUnsplashTests
@Suite(.enabled(if: ProcessInfo.processInfo.environment["ORIEL_LIVE_KEY"] != nil))
struct LiveUnsplashTests {
    @Test func searchesAndDownloadsARealPhoto() async throws {
        let key = ProcessInfo.processInfo.environment["ORIEL_LIVE_KEY"]
        let provider = UnsplashProvider(keys: FakeKeyStore(key: key))

        let page = try await provider.search("mountains", page: 1)
        let photo = try #require(page.photos.first)
        #expect(photo.isLandscape)
        #expect(photo.photoPageURL.absoluteString.contains("utm_source=oriel"))

        let jpeg = try await provider.download(photo, size: PixelSize(width: 640, height: 360))
        #expect(jpeg.starts(with: [0xFF, 0xD8])) // a JPEG
        #expect(await provider.remainingRequests != nil)
    }
}
