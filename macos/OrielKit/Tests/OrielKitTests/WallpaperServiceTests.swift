import Foundation
import Testing
@testable import OrielKit

struct WallpaperServiceTests {
    let dir = TempDir()
    let setter = FakeSetter()
    let store: JSONSettingsStore
    let history: SQLiteHistoryStore
    let clock = FakeClock(.utc(2026, 10, 3, 14))

    init() throws {
        try dir.paths.ensureCreated()
        store = JSONSettingsStore(paths: dir.paths)
        history = try SQLiteHistoryStore(paths: dir.paths)
        store.update { $0.matchTimeOfDay = false }
    }

    func service(_ provider: FakeProvider) -> WallpaperService {
        WallpaperService(provider: provider, setter: setter, history: history, settings: store, clock: clock, paths: dir.paths)
    }

    @Test func appliesRecordsAndTracks() async throws {
        let provider = FakeProvider([[Fixtures.photo("a")]])
        let svc = service(provider)
        let result = await svc.apply(force: false)
        await svc.afterChangeDone()

        #expect(result.status == .applied)
        let entry = try #require(result.entries.first)
        #expect(FileManager.default.fileExists(atPath: entry.filePath))
        #expect(setter.current(on: nil) == entry.fileURL)
        #expect(history.wasUsed(provider: "unsplash", photoID: "a"))
        #expect(await provider.tracked == ["a"])
        #expect(store.loadState().lastAppliedDate == clock.today)
    }

    @Test func neverRepeatsAndSkipsFilteredPhotos() async throws {
        try history.add(Fixtures.entry("used", at: .utc(2026, 1, 1), file: URL(filePath: "/tmp/used.jpg")))
        let provider = FakeProvider([[Fixtures.photo("used"), Fixtures.photo("tall", width: 3000, height: 5000), Fixtures.photo("fresh")]])

        let result = await service(provider).apply(force: true)

        #expect(result.entries.map(\.photoID) == ["fresh"])
    }

    @Test func aFailureKeepsTheWallpaperAndSchedulesARetry() async {
        let provider = FakeProvider()
        await provider.fail(with: ProviderError(.network, "offline"))

        let result = await service(provider).apply(force: false)

        #expect(result.status == .failed)
        #expect(setter.calls.isEmpty)
        let state = store.loadState()
        #expect(state.consecutiveFailures == 1)
        #expect(state.nextRetryAt == clock.now.addingTimeInterval(120))
    }

    @Test func todaysChangeHappensOnce() async {
        let svc = service(FakeProvider([[Fixtures.photo("a")], [Fixtures.photo("b")]]))
        #expect(await svc.apply(force: false).status == .applied)
        #expect(await svc.apply(force: false).status == .alreadyAppliedToday)
    }

    @Test func changeNowIsLimitedPerHour() async {
        let svc = service(FakeProvider([[Fixtures.photo("a")], [Fixtures.photo("b")], [Fixtures.photo("c")]]))
        #expect(await svc.apply(force: true).status == .applied)
        #expect(await svc.apply(force: true).status == .applied)
        let third = await svc.apply(force: true)
        #expect(third.status == .rateLimited)
        #expect(third.error?.contains("60 min") == true)
    }

    @Test func pausedSkipsTheSchedule() async {
        store.update { $0.paused = true }
        #expect(await service(FakeProvider()).apply(force: false).status == .paused)
    }

    @Test func eachScreenCanGetItsOwnPhoto() async {
        setter.addScreen("SCREEN2", x: 2560)
        store.update { $0.photoPerMonitor = true }
        let svc = service(FakeProvider([[Fixtures.photo("a")], [Fixtures.photo("b")]]))

        let result = await svc.apply(force: true)

        #expect(result.entries.count == 2)
        #expect(Set(setter.calls.compactMap(\.screen)) == ["SCREEN1", "SCREEN2"])
    }

    /// Capped at 3840 wide (keeping the shape), so 5K/6K displays don't reject most photos.
    @Test func spanSizesForTheWholeDesktop() {
        var s = OrielSettings()
        s.fit = .span
        let screens = [ScreenInfo(id: "1", frame: PixelRect(x: 0, y: 0, width: 2560, height: 1440), isMain: true),
                       ScreenInfo(id: "2", frame: PixelRect(x: 2560, y: 0, width: 1920, height: 1080), isMain: false)]
        #expect(WallpaperService.planTargets(screens, s).map(\.size) == [PixelSize(width: 3840, height: 1234)])
    }

    @Test func favoritesOnlyReusesAFavorite() async throws {
        let file = Fixtures.file("fav.jpg", in: dir.paths.picturesRoot)
        let id = try history.add(Fixtures.entry("fav", at: .utc(2026, 1, 1), file: file))
        history.setFavorite(id, true)
        store.update { $0.favoritesOnly = true }
        let provider = FakeProvider()

        let result = await service(provider).apply(force: true)

        #expect(result.entries.map(\.photoID) == ["fav"])
        #expect(await provider.queries.isEmpty)
    }
}

struct ScheduleRulesTests {
    func settings(_ time: String = "09:00", interval: Int = 0) -> OrielSettings {
        var s = OrielSettings()
        s.dailyTime = time
        s.timeZone = "Asia/Tashkent" // UTC+5
        s.intervalMinutes = interval
        return s
    }

    @Test func dailyTicksAtTheChosenTime() {
        let s = settings()
        let now = Date.utc(2026, 10, 3, 3) // 08:00 Tashkent
        #expect(ScheduleRules.previousTick(s, now) == .utc(2026, 10, 2, 4))
        #expect(ScheduleRules.nextTick(s, now) == .utc(2026, 10, 3, 4))
    }

    @Test func slideshowTicksEveryIntervalFromTheDailyTime() {
        let s = settings(interval: 120)
        let now = Date.utc(2026, 10, 3, 7) // 12:00 Tashkent
        #expect(ScheduleRules.previousTick(s, now) == .utc(2026, 10, 3, 6)) // 09:00, 11:00 Tashkent...
        #expect(ScheduleRules.nextTick(s, now) == .utc(2026, 10, 3, 8))
    }

    @Test func dueOnceATickPassesAndNotAfterwards() {
        let s = settings()
        var state = OrielState()
        state.lastAppliedAt = .utc(2026, 10, 2, 5)
        #expect(!ScheduleRules.isDue(s, state, .utc(2026, 10, 3, 3), isStartupCheck: false))
        #expect(ScheduleRules.isDue(s, state, .utc(2026, 10, 3, 4), isStartupCheck: false))
        state.lastAppliedAt = .utc(2026, 10, 3, 4)
        #expect(!ScheduleRules.isDue(s, state, .utc(2026, 10, 3, 6), isStartupCheck: false))
    }

    @Test func missedChangeAtLoginOnlyWhenAllowed() {
        var s = settings()
        s.applyAtLogin = false
        #expect(!ScheduleRules.isDue(s, OrielState(), .utc(2026, 10, 3, 6), isStartupCheck: true))
        #expect(ScheduleRules.isDue(s, OrielState(), .utc(2026, 10, 3, 6), isStartupCheck: false))
    }

    @Test func sleepsAtMostTenMinutes() {
        var state = OrielState()
        state.lastAppliedAt = .utc(2026, 10, 3, 4)
        #expect(ScheduleRules.nextCheckIn(settings(), state, .utc(2026, 10, 3, 5)) == 600)
        #expect(ScheduleRules.nextCheckIn(settings(), state, Date.utc(2026, 10, 4, 3).addingTimeInterval(59 * 60)) <= 600)
    }
}
