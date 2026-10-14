import Foundation
import Testing
@testable import OrielKit

/// The schedule vectors from the spec (§13) and Windows' SchedulingTests: every platform must compute the same ticks.
struct ScheduleVectorTests {
    static let zone = TimeZone(identifier: "Asia/Tashkent")!

    /// 2026-10-03 at hh:mm in the test's zone.
    func at(_ hour: Int, _ minute: Int = 0, day: Int = 3) -> Date {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = Self.zone
        return c.date(from: DateComponents(year: 2026, month: 10, day: day, hour: hour, minute: minute))!
    }

    func settings(daily: String = "09:00", interval: Int = 0) -> OrielSettings {
        var s = OrielSettings()
        s.dailyTime = daily
        s.timeZone = Self.zone.identifier
        s.intervalMinutes = interval
        return s
    }

    func state(last: Date? = nil, started: Date? = nil, retry: Date? = nil) -> OrielState {
        var st = OrielState()
        st.lastAppliedAt = last
        st.scheduleStartedAt = started
        st.nextRetryAt = retry
        return st
    }

    @Test func hourlySlideshow() {
        let s = settings(interval: 60)
        let st = state(last: at(9))
        #expect(!ScheduleRules.isDue(s, st, at(9, 30), isStartupCheck: false))
        #expect(!ScheduleRules.isDue(s, st, at(9, 59), isStartupCheck: false))
        #expect(ScheduleRules.isDue(s, st, at(10), isStartupCheck: false))
        #expect(ScheduleRules.nextCheckIn(s, st, at(9, 50)) == 600)
        #expect(ScheduleRules.slotId(s, ScheduleRules.dueTick(s, at(10))) == "2026-10-03T10:00")
    }

    @Test func dailyCatchesUpAMissedChange() {
        let s = settings()
        #expect(ScheduleRules.isDue(s, OrielState(), at(7), isStartupCheck: true))
        #expect(ScheduleRules.isDue(s, OrielState(), at(7), isStartupCheck: false))
        #expect(!ScheduleRules.isDue(s, state(last: at(9, 1)), at(15), isStartupCheck: false))
        #expect(!ScheduleRules.isDue(s, state(last: at(8)), at(8, 50), isStartupCheck: false))
        #expect(ScheduleRules.isDue(s, state(last: at(8)), at(9), isStartupCheck: false))
    }

    @Test func noCatchUpAtLoginWhenTurnedOff() {
        var s = settings()
        s.applyAtLogin = false
        #expect(!ScheduleRules.isDue(s, OrielState(), at(7), isStartupCheck: true))
    }

    @Test func anEditNeverChangesTheWallpaperByItself() {
        #expect(!ScheduleRules.isDue(settings(), state(last: at(8), started: at(9, 30)), at(9, 40), isStartupCheck: false))
    }

    @Test func pausedFollowingAndRetries() {
        var paused = settings()
        paused.paused = true
        #expect(!ScheduleRules.isDue(paused, OrielState(), at(10), isStartupCheck: false))
        var following = settings()
        following.followUid = "friend"
        #expect(!ScheduleRules.isDue(following, OrielState(), at(10), isStartupCheck: false))
        let retry = state(last: at(8), retry: at(10, 5))
        #expect(!ScheduleRules.isDue(settings(), retry, at(10), isStartupCheck: false))
        #expect(ScheduleRules.isDue(settings(), retry, at(10, 6), isStartupCheck: false))
    }

    @Test func nextCheckInVectors() {
        let s = settings()
        #expect(ScheduleRules.nextCheckIn(s, state(last: at(8)), at(8, 56)) == 4 * 60)
        #expect(ScheduleRules.nextCheckIn(s, state(last: at(6)), at(7)) == 600)
        #expect(ScheduleRules.nextCheckIn(s, state(last: at(8), retry: at(10, 3)), at(10)) == 3 * 60)
        #expect(ScheduleRules.nextCheckIn(s, state(last: at(9, 1)), at(10)) == 600)
    }

    @Test func tokyoTicksAndSlot() {
        var s = OrielSettings()
        s.timeZone = "Asia/Tokyo"
        s.dailyTime = "09:00"
        let now = Date.utc(2026, 10, 3, 0).addingTimeInterval(30 * 60)
        #expect(ScheduleRules.previousTick(s, now) == .utc(2026, 10, 3, 0))
        #expect(ScheduleRules.nextTick(s, now) == .utc(2026, 10, 4, 0))
        #expect(ScheduleRules.slotId(s, ScheduleRules.previousTick(s, now)) == "2026-10-03T09:00")
    }

    @Test func aTimeSkippedByDSTBecomesTheHourAfter() {
        var s = OrielSettings()
        s.timeZone = "America/New_York"
        s.dailyTime = "02:30"
        // 2027-03-14: clocks jump from 02:00 to 03:00 in New York; 03:30 EDT = 07:30 UTC.
        let tick = ScheduleRules.nextTick(s, Date.utc(2027, 3, 14, 5))
        #expect(tick == Date.utc(2027, 3, 14, 7).addingTimeInterval(30 * 60))
    }
}

struct DeepLinkTests {
    @Test(arguments: [
        ("oriel://auth?token=abc.def-ghi", "abc.def-ghi"),
        ("\"oriel://auth?token=a%2Bb%3D\"", "a+b="),
        ("ORIEL://AUTH?token=x", "x"),
    ])
    func readsTheGoogleToken(link: String, expected: String) {
        #expect(DeepLink.googleToken(["--background", link]) == expected)
    }

    @Test(arguments: ["oriel://auth", "oriel://auth?token=", "oriel://other?token=x", "https://auth?token=x", "--background"])
    func ignoresLinksWithoutAToken(link: String) {
        #expect(DeepLink.googleToken([link]) == nil)
    }

    @Test func readsAnEmailSignInAndWhatItCarries() {
        let link = "oriel://auth?refresh=AMf-vBx_1%2B2&share=AbC123xyz&from=Akbar%20K&follow=k7mq-2xpa"
        #expect(DeepLink.sessionToken([link]) == "AMf-vBx_1+2")
        #expect(DeepLink.googleToken([link]) == nil)
        #expect(DeepLink.pendingShare([link])?.id == "AbC123xyz")
        #expect(DeepLink.pendingShare([link])?.from == "Akbar")
        #expect(DeepLink.followCode([link]) == "K7MQ2XPA")
    }

    @Test(arguments: [
        ("oriel://wallpaper?id=AbC123xyz", "AbC123xyz"),
        ("oriel://WALLPAPER?id=a-b_c123", "a-b_c123"),
        ("oriel://wallpaper?id=../etc", nil),
        ("oriel://wallpaper?id=ab", nil),
        ("oriel://auth?token=x", nil),
    ] as [(String, String?)])
    func sharedPhotoLinks(link: String, expected: String?) {
        #expect(DeepLink.sharedPhoto([link])?.id == expected)
    }

    @Test func openAndAnyLink() {
        #expect(DeepLink.isOpen(["oriel://open"]))
        #expect(DeepLink.isLink(["oriel://auth"]))
        #expect(!DeepLink.isLink(["--background"]))
        #expect(DeepLink.followCode(["oriel://follow?code=K7MQ2XP0"]) == nil) // 0 is never used
    }
}

struct ShareAndCodeTests {
    func entry(thumb: String, photographer: String = "https://unsplash.com/@jane_doe") -> HistoryEntry {
        HistoryEntry(provider: "unsplash", photoID: "abc", filePath: "", thumbnailURL: thumb, photoPageURL: "p",
                     photographerName: "Jane Doe", photographerURL: photographer, appliedAt: Date())
    }

    @Test func photoOnlyForUnsplashImages() {
        let p = ShareLink.photo(entry(thumb: "https://images.unsplash.com/photo-1428908728789-d2de25dbd4e2?ixid=x&w=400"))
        #expect(p?.path == "photo-1428908728789-d2de25dbd4e2")
        #expect(p?.handle == "jane_doe")
        #expect(ShareLink.photo(entry(thumb: "https://example.com/photo-123456")) == nil)
        #expect(ShareLink.photo(entry(thumb: "https://images.unsplash.com/a/b?x")) == nil)
        #expect(ShareLink.photo(entry(thumb: "https://images.unsplash.com/photo-123456", photographer: "https://evil/@x"))?.handle == "")
    }

    @Test(arguments: [
        ("https://lh3.googleusercontent.com/a/ACg8oc=s96-c", "https://lh3.googleusercontent.com/a/ACg8oc=s96-c"),
        ("https://evil.example/a.png", ""),
        ("http://lh3.googleusercontent.com/a/x", ""),
    ])
    func pictureOnlyFromGoogle(url: String, expected: String) {
        #expect(ShareLink.picture(url) == expected)
    }

    @Test func cleanNames() {
        #expect(ShareLink.cleanName(nil) == "")
        #expect(ShareLink.cleanName("  ") == "")
        #expect(ShareLink.cleanName("<b>Ann</b> Lee") == "bAnn/b")
        #expect(ShareLink.cleanName("Akbarjon Karimov") == "Akbarjon")
    }

    @Test func followCodesAreForgivingToType() {
        #expect(FollowCodes.normalize("k7mq-2xpa") == "K7MQ2XPA")
        #expect(FollowCodes.normalize(" K7MQ 2XPA ") == "K7MQ2XPA")
        #expect(FollowCodes.normalize("K7MQ2XP") == nil)
        #expect(FollowCodes.normalize("K7MQ2XP0") == nil)
        #expect(FollowCodes.format("K7MQ2XPA") == "K7MQ-2XPA")
        #expect(FollowCodes.formatInput("k7mq2x") == "K7MQ-2X")
        #expect(FollowCodes.formatInput("k7mq-2xpa99") == "K7MQ-2XPA")
        #expect(FollowCodes.normalize(FollowCodes.new()) != nil)
    }
}

struct TodayAndStatsTests {
    @Test func pickFollowsDaysSince1970AndWrapsAround() {
        var tashkent = Calendar(identifier: .gregorian)
        tashkent.timeZone = TimeZone(identifier: "Asia/Tashkent")!
        let list = Array(0..<7)
        // 2026-10-14 local = day 20740 since 1970; 20740 % 7 = 6.
        let day = tashkent.date(from: DateComponents(year: 2026, month: 10, day: 14, hour: 1))!
        #expect(TodayService.pick(list, on: day, calendar: tashkent) == 20740 % 7)
        let before1970 = tashkent.date(from: DateComponents(year: 1969, month: 12, day: 31, hour: 12))!
        #expect(TodayService.pick(list, on: before1970, calendar: tashkent) == 6)
    }

    @Test func parsesTheList() {
        let json = #"{"rule":"x","photos":[{"id":"a","path":"photo-1","by":"Ann","handle":"ann"},{"id":"","path":"p"}]}"#
        #expect(TodayService.parse(Data(json.utf8)).map(\.id) == ["a"])
    }

    @Test func statsCountNewActiveAndRetention() throws {
        let today = LocalDate(year: 2026, month: 10, day: 14)
        let first = try #require(StatsMemory.forActiveDay(StatsMemory(), today: today, platform: "macos", installedOn: nil))
        #expect(first.fields == ["macos_active", "macos_new"])
        #expect(StatsMemory.forActiveDay(first.next, today: today, platform: "macos", installedOn: nil) == nil)

        let existing = try #require(StatsMemory.forActiveDay(StatsMemory(), today: today, platform: "macos",
                                                             installedOn: LocalDate(year: 2026, month: 9, day: 1)))
        #expect(existing.fields == ["macos_active", "retained7", "retained30"])
    }
}

struct AccentPickerTests {
    func pixels(_ r: UInt8, _ g: UInt8, _ b: UInt8, count: Int = 100) -> [UInt8] {
        Array((0..<count).flatMap { _ in [r, g, b, 255] })
    }

    @Test func aSaturatedColorWins() {
        let blue = AccentPicker.pick(pixels(20, 60, 220) + pixels(240, 240, 240, count: 300))
        #expect(blue.b > blue.r && blue.b > blue.g)
    }

    @Test func grayscaleUsesItsAverageAndIsKeptReadable() {
        let gray = AccentPicker.pick(pixels(128, 128, 128))
        #expect(gray.r == gray.g && gray.g == gray.b)
        #expect(Double(gray.r) / 255 >= 0.54 && Double(gray.r) / 255 <= 0.86)
    }

    @Test func noPixelsIsWindowsBlue() {
        #expect(AccentPicker.hex(AccentPicker.pick([])) == "#0078D4")
    }
}

struct SyncedSettingsTests {
    @Test func applyingKeepsLocalCoversAndBuiltIns() {
        var local = OrielSettings()
        local.topics.append(TopicSetting(query: "tokyo night", enabled: true, isCustom: true, coverURL: "https://cover"))
        var remote = SyncedSettings.from(local)
        remote.dailyTime = "07:15"
        remote.paused = true
        remote.interests = remote.interests.map { var i = $0; i.enabled = i.query == "fog" || i.isCustom; return i }

        remote.apply(to: &local)

        #expect(local.dailyTime == "07:15")
        #expect(local.paused)
        #expect(local.enabledQueries().sorted() == ["fog", "tokyo night"])
        #expect(local.topics.first { $0.query == "tokyo night" }?.coverURL == "https://cover")
        #expect(SyncedSettings.from(local).fingerprint == remote.fingerprint)
    }

    @Test func scheduleChangesAreSpotted() {
        let a = SyncedSettings.from(OrielSettings())
        var b = a
        b.minLikes = 300
        #expect(a.sameSchedule(b))
        b.intervalMinutes = 60
        #expect(!a.sameSchedule(b))
    }
}

struct FirebaseClientTests {
    @Test func readsSharedSettingsWithDefaults() async throws {
        let http = StubHTTP { request in
            StubHTTP.json(#"""
            {"fields": {"dailyTime": {"stringValue": "08:30"}, "intervalMinutes": {"integerValue": "180"},
              "paused": {"booleanValue": true},
              "interests": {"arrayValue": {"values": [
                {"mapValue": {"fields": {"query": {"stringValue": "fog"}, "enabled": {"booleanValue": true}}}},
                {"mapValue": {"fields": {"enabled": {"booleanValue": true}}}}]}},
              "updatedAt": {"timestampValue": "2026-10-14T09:00:00.1234567Z"}}}
            """#, for: request)
        }
        let tokens = FirebaseTokens(idToken: "id", refreshToken: "r", expiresAt: .distantFuture, uid: "u1")
        let (s, at) = try #require(try await FirebaseClient(http: http).syncedSettings(tokens))
        #expect(s.dailyTime == "08:30")
        #expect(s.timeZone == "UTC")
        #expect(s.intervalMinutes == 180)
        #expect(s.paused)
        #expect(s.minLikes == 50)
        #expect(s.matchTimeOfDay) // missing means on
        #expect(s.interests.map(\.query) == ["fog"])
        #expect(at > .utc(2026, 10, 14, 8))
        #expect(http.requests.first?.value(forHTTPHeaderField: "Authorization") == "Bearer id")
    }

    @Test func mapsErrors() async throws {
        let http = StubHTTP { StubHTTP.json(#"{"error": {"status": "FAILED_PRECONDITION", "message": "no"}}"#, status: 400, for: $0) }
        let tokens = FirebaseTokens(idToken: "id", refreshToken: "r", expiresAt: .distantFuture, uid: "u1")
        #expect(try await FirebaseClient(http: http).touchSession(tokens, sessionId: "s", appVersion: "1") == false)

        let offline = StubHTTP { _ in throw URLError(.notConnectedToInternet) }
        do {
            _ = try await FirebaseClient(http: offline).hasCurrentWallpaper(tokens)
            Issue.record("Expected an error")
        } catch let error as FirebaseError {
            #expect(error.failure == .network)
            #expect(error.message == "Couldn't reach Google. Check your connection.")
        } catch {
            Issue.record("Unexpected \(error)")
        }
    }

    @Test func pulseRingsArePatches() async throws {
        let http = StubHTTP { StubHTTP.json("{}", for: $0) }
        let tokens = FirebaseTokens(idToken: "id/+", refreshToken: "r", expiresAt: .distantFuture, uid: "u1")
        try await FirebaseClient(http: http).ring(tokens, photoId: "p1", source: "macos", device: "d1")
        let request = try #require(http.requests.first)
        #expect(request.httpMethod == "PATCH")
        #expect(request.url?.absoluteString.hasPrefix("https://oriel-gcp-default-rtdb.firebaseio.com/pulse/u1.json?auth=") == true)
        let body = try #require(try JSONSerialization.jsonObject(with: request.httpBody ?? Data()) as? [String: Any])
        #expect(body["source"] as? String == "macos")
        #expect((body["at"] as? [String: String])?[".sv"] == "timestamp")
    }
}

struct AccountStoreTests {
    @Test func storedOwnerOnlyAndReadBack() throws {
        let dir = TempDir()
        try dir.paths.ensureCreated()
        let store = FileAccountStore(paths: dir.paths)
        #expect(store.load() == nil)

        var account = Account()
        account.uid = "u1"
        account.email = "a@b.c"
        account.refreshToken = "secret-refresh-token"
        account.sessionId = "s1"
        store.save(account)

        let mode = try FileManager.default.attributesOfItem(atPath: dir.paths.accountFile.path(percentEncoded: false))[.posixPermissions] as? Int
        #expect(mode == 0o600)
        #expect(store.load()?.refreshToken == "secret-refresh-token")
        store.clear()
        #expect(store.load() == nil)
    }
}

struct SignInRequiredTests {
    struct SignedOut: AuthState { var isSignedIn: Bool { false } }

    @Test func nothingChangesWhileSignedOut() async throws {
        let dir = TempDir()
        try dir.paths.ensureCreated()
        let svc = WallpaperService(provider: FakeProvider([[Fixtures.photo("a")]]), setter: FakeSetter(),
                                   history: try SQLiteHistoryStore(paths: dir.paths), settings: JSONSettingsStore(paths: dir.paths),
                                   paths: dir.paths, auth: SignedOut())
        let result = await svc.apply(force: true)
        #expect(result.status == .signInRequired)
        #expect(result.error == "Sign in to Oriel first.")
    }

    struct LosesSlot: SlotClaiming { func tryClaim(_ slotId: String) async -> Bool? { false } }

    @Test func anotherDeviceTookTheTick() async throws {
        let dir = TempDir()
        try dir.paths.ensureCreated()
        let store = JSONSettingsStore(paths: dir.paths)
        let setter = FakeSetter()
        let svc = WallpaperService(provider: FakeProvider([[Fixtures.photo("a")]]), setter: setter,
                                   history: try SQLiteHistoryStore(paths: dir.paths), settings: store, paths: dir.paths, slots: LosesSlot())
        #expect(await svc.apply(force: false).status == .takenByOtherDevice)
        #expect(setter.calls.isEmpty)
        #expect(store.loadState().scheduleStartedAt != nil)
    }
}
