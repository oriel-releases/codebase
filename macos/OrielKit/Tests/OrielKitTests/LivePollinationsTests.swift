import Foundation
import Testing
@testable import OrielKit

/// Makes one real AI wallpaper (costs a little Pollen). Off by default:
///   ORIEL_LIVE_POLLINATIONS=<key> scripts/test.sh --filter LivePollinationsTests
@Suite(.enabled(if: ProcessInfo.processInfo.environment["ORIEL_LIVE_POLLINATIONS"] != nil))
struct LivePollinationsTests {
    @Test func makesARealPicture() async throws {
        let dir = TempDir()
        try dir.paths.ensureCreated()
        let client = PollinationsClient(keys: FakeKeyStore(key: ProcessInfo.processInfo.environment["ORIEL_LIVE_POLLINATIONS"]),
                                        paths: dir.paths)
        let (photo, image) = try await client.create(prompt: "a cozy cabin in a snowy forest at night", style: nil,
                                                     seed: PollinationsClient.newSeed(), screen: PixelSize(width: 3024, height: 1964))
        #expect(photo.id.hasPrefix("ai-"))
        #expect(PollinationsClient.isImage(image))
        print("LIVE: \(photo.rawURL.absoluteString) \(image.count) bytes")
    }
}
