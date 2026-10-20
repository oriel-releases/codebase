import Foundation
import Testing
@testable import OrielKit

struct TopColorTests {
    func solid(_ r: UInt8, _ g: UInt8, _ b: UInt8, _ count: Int) -> [UInt8] {
        Array((0..<count).flatMap { _ in [r, g, b, 255] })
    }

    @Test func topColorsComeFromDifferentHuesMostUsedFirst() {
        let pixels = solid(20, 60, 220, 600) + solid(230, 120, 20, 300) + solid(40, 180, 60, 100)
            + solid(30, 110, 230, 200) // next to blue: not offered on its own
        let top = AccentPicker.pickTop(pixels)
        #expect(top.count == 3)
        #expect(top[0].b > top[0].r, "blue first")
        #expect(top[1].r > top[1].b, "orange second")
        #expect(top[2].g > top[2].r && top[2].g > top[2].b, "green third")
        let first = AccentPicker.pick(pixels)
        #expect(first == top[0])
    }

    @Test func oneColorAndGrayStillOfferThree() {
        let blue = AccentPicker.pickTop(solid(20, 90, 220, 1000)).map(AccentPicker.hex)
        #expect(Set(blue).count == 3)
        let gray = AccentPicker.pickTop(solid(100, 100, 100, 500)).map(AccentPicker.hex)
        #expect(Set(gray).count == 3)
    }
}

struct PollinationsTests {
    static let link = "https://media.pollinations.ai/e774ccc41b39159943fc0e2073130ce013fa363cbc05a4ed3058bcbdc677218d"

    @Test(arguments: [
        (PixelSize(width: 3840, height: 2160), PixelSize(width: 2048, height: 1152)),
        (PixelSize(width: 1920, height: 1080), PixelSize(width: 1920, height: 1088)),
        (PixelSize(width: 3440, height: 1440), PixelSize(width: 2048, height: 864)),
        // 16:10 screens: within the model's pixel limit (2048 × 1328 is refused).
        (PixelSize(width: 3024, height: 1964), PixelSize(width: 1904, height: 1232)),
        (PixelSize(width: 2560, height: 1600), PixelSize(width: 1936, height: 1216)),
    ])
    func picturesKeepTheScreenShapeAtMost2048(screen: PixelSize, expected: PixelSize) {
        #expect(PollinationsClient.sizeFor(screen) == expected)
    }

    @Test func aMediaLinkBecomesAnAIPhotoAnyDeviceCanDownload() throws {
        let photo = try #require(PollinationsClient.fromLink(Self.link + "?w=640&q=80", prompt: "cat on moon"))
        #expect(photo.id == "ai-e774ccc41b39159943fc0e2073130ce013fa363cbc05a4ed3058bcbdc677218d")
        #expect(photo.provider == "ai")
        #expect(photo.rawURL.absoluteString == Self.link) // sizing added by other apps is dropped
        #expect(photo.photographerName == "Pollinations AI")
        #expect(PollinationsClient.isAiPhoto(photo.id))
    }

    @Test(arguments: [
        "https://images.unsplash.com/photo-123",
        "http://media.pollinations.ai/e774ccc41b39159943fc0e2073130ce0",
        "https://media.pollinations.ai/not-an-id",
        "https://evil.example/e774ccc41b39159943fc0e2073130ce0",
    ])
    func otherLinksAreNotAIPhotos(link: String) {
        #expect(PollinationsClient.fromLink(link, prompt: nil) == nil)
    }

    @Test func theFinalPromptOnlySaysWhatShouldBeThere() {
        let prompt = PollinationsClient.final("A misty lake at dawn, pines on the shore.", "lake at dawn.")
        #expect(prompt.hasPrefix("Full-bleed widescreen wallpaper"))
        #expect(prompt.contains("A misty lake at dawn, pines on the shore. User's idea: lake at dawn."))
        // Whole words ("wallpaper" is fine): the model draws what it's told about, even "no border".
        let words = Set(prompt.lowercased().split { !$0.isLetter }.map(String.init))
        for word in ["border", "borders", "frame", "frames", "paper"] { #expect(!words.contains(word)) }
    }

    @Test func createsExpandsPaintsAndCountsOne() async throws {
        let dir = TempDir()
        try dir.paths.ensureCreated()
        var png = Data([0x89, 0x50, 0x4E, 0x47])
        png.append(Data(count: 4096))
        let http = StubHTTP { [png] request in
            let url = request.url!.absoluteString
            if url.hasSuffix("/chat/completions") {
                return StubHTTP.json(#"{"choices":[{"message":{"content":"\"A cabin glowing in a snowy pine forest at night.\""}}]}"#, for: request)
            }
            if url.hasSuffix("/images/generations") {
                return StubHTTP.json(#"{"data":[{"url":"\#(Self.link)"}]}"#, for: request)
            }
            return (png, HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!)
        }
        let client = PollinationsClient(http: http, keys: FakeKeyStore(key: "sk_test"), paths: dir.paths)
        #expect(client.leftToday == 20)

        let (photo, image) = try await client.create(prompt: "cabin in snow", style: "pixel art, retro game scene", seed: 7,
                                                     screen: PixelSize(width: 3024, height: 1964))

        #expect(photo.id.hasPrefix("ai-"))
        #expect(PollinationsClient.isPNG(image))
        #expect(client.leftToday == 19)
        let paint = try #require(http.requests.first { $0.url!.absoluteString.hasSuffix("/images/generations") })
        #expect(paint.value(forHTTPHeaderField: "Authorization") == "Bearer sk_test")
        let body = try #require(try JSONSerialization.jsonObject(with: paint.httpBody!) as? [String: Any])
        #expect(body["model"] as? String == "zimage")
        #expect(body["size"] as? String == "1904x1232")
        #expect((body["prompt"] as? String)?.contains("User's idea: cabin in snow.") == true)
        #expect((body["prompt"] as? String)?.contains("A cabin glowing") == true)
    }

    @Test func noKeyAndTheDailyLimitSayWhy() async throws {
        let dir = TempDir()
        try dir.paths.ensureCreated()
        let none = PollinationsClient(http: StubHTTP { StubHTTP.json("{}", for: $0) }, keys: FakeKeyStore(key: nil), paths: dir.paths)
        await #expect(throws: ProviderError(.missingKey, "This build of Oriel can't create AI wallpapers.")) {
            _ = try await none.create(prompt: "x", style: nil, seed: 1, screen: PixelSize(width: 1920, height: 1080))
        }
        let today = (0..<20).map { _ in Date() }
        try JSONSettingsStore.encoder.encode(today).write(to: dir.paths.dataRoot.appending(path: "created.json"))
        let full = PollinationsClient(http: StubHTTP { StubHTTP.json("{}", for: $0) }, keys: FakeKeyStore(key: "k"), paths: dir.paths)
        #expect(full.leftToday == 0)
        await #expect(throws: ProviderError.self) {
            _ = try await full.create(prompt: "x", style: nil, seed: 1, screen: PixelSize(width: 1920, height: 1080))
        }
    }

    @Test func statusCodesBecomeWords() {
        #expect(PollinationsClient.error(402, Data()).kind == .rateLimited)
        #expect(PollinationsClient.error(429, Data()).message == "That was a lot of pictures in a short time. Wait a minute and try again.")
        #expect(PollinationsClient.error(400, Data()).message == "Pollinations couldn't make that. Try other words.")
    }
}

struct AIWallpaperTests {
    @Test func aCreatedPictureIsSavedAsPNGSetAndRecordedWithoutTracking() async throws {
        let dir = TempDir()
        try dir.paths.ensureCreated()
        let setter = FakeSetter()
        let history = try SQLiteHistoryStore(paths: dir.paths)
        let provider = FakeProvider()
        let svc = WallpaperService(provider: provider, setter: setter, history: history, settings: JSONSettingsStore(paths: dir.paths),
                                   paths: dir.paths)
        let photo = try #require(PollinationsClient.fromLink(PollinationsTests.link, prompt: "cat on moon"))
        var png = Data([0x89, 0x50, 0x4E, 0x47])
        png.append(Data(count: 4096))

        let result = await svc.applyCreated(photo, image: png)
        await svc.afterChangeDone()

        let entry = try #require(result.entries.first)
        #expect(result.status == .applied)
        #expect(entry.filePath.hasSuffix(".png"))
        #expect(entry.provider == "ai")
        #expect(entry.topic == "created")
        #expect(setter.current(on: nil) == entry.fileURL)
        #expect(await provider.tracked.isEmpty) // only Unsplash counts downloads
    }

    @Test func theChosenAccentIsKeptPerPhoto() throws {
        let dir = TempDir()
        try dir.paths.ensureCreated()
        let svc = WallpaperService(provider: FakeProvider(), setter: FakeSetter(), history: try SQLiteHistoryStore(paths: dir.paths),
                                   settings: JSONSettingsStore(paths: dir.paths), paths: dir.paths)
        svc.rememberAccent("p1", "#aabbcc")
        #expect(svc.chosenAccent("p1") == "#AABBCC")
        #expect(svc.chosenAccent("p2") == nil)
    }

    @Test func aiFavoritesSyncWithTheirLinkAsItIs() throws {
        let entry = HistoryEntry(provider: "ai", photoID: "ai-e774ccc41b39159943fc0e2073130ce0", filePath: "",
                                 thumbnailURL: "https://media.pollinations.ai/e774ccc41b39159943fc0e2073130ce0", photoPageURL: "p",
                                 photographerName: "Pollinations AI", photographerURL: "https://pollinations.ai", appliedAt: Date(),
                                 isFavorite: true)
        let synced = try #require(FavoritesSync.toSynced(entry, at: Date()))
        #expect(synced.imageUrl == entry.thumbnailURL)
        #expect(SyncPublisher.document(entry)?.imageUrl == entry.thumbnailURL)
        #expect(ShareLink.photo(entry) == nil) // share links stay Unsplash-only
    }
}
