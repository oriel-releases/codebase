import Foundation
import Testing
@testable import OrielKit

struct BasicsTests {
    @Test func backoffGrowsAndCaps() {
        #expect(RetryBackoff.delay(afterFailures: 1) == 2 * 60)
        #expect(RetryBackoff.delay(afterFailures: 3) == 15 * 60)
        #expect(RetryBackoff.delay(afterFailures: 99) == 60 * 60)
        #expect(RetryBackoff.delay(afterFailures: 0) == 2 * 60)
    }

    @Test func topicPickerAvoidsUsedTopicsWhenPossible() {
        let picker = RandomTopicPicker()
        for _ in 0..<20 { #expect(picker.pick(["a", "b"], avoiding: ["a"]) == "b") }
        #expect(picker.pick(["a"], avoiding: ["a"]) == "a")
        #expect(picker.pick([], avoiding: []) == RandomTopicPicker.fallback)
    }

    @Test func filterRejectsPortraitSmallAndUnlikedPhotos() {
        var s = OrielSettings()
        let screen = PixelSize(width: 2560, height: 1440)
        #expect(CandidateFilter.reject(Fixtures.photo("p", width: 3000, height: 4000), target: screen, settings: s) == "portrait")
        #expect(CandidateFilter.reject(Fixtures.photo("s", width: 1920, height: 1080), target: screen, settings: s) == "too small")
        #expect(CandidateFilter.reject(Fixtures.photo("ok", likes: 3), target: screen, settings: s) == nil)
        s.skipLowLikes = true
        #expect(CandidateFilter.reject(Fixtures.photo("l", likes: 3), target: screen, settings: s) == "low likes")
    }

    @Test(arguments: [(21, false, Mood.dark), (3, false, .dark), (8, false, .bright), (14, false, .any), (14, true, .dark)])
    func moodFollowsTheHourAndDarkMode(hour: Int, dark: Bool, expected: Mood) {
        let clock = FakeClock(.utc(2026, 10, 3, hour))
        #expect(PhotoMood.mood(at: clock.now, calendar: clock.calendar, systemDark: dark) == expected)
    }

    @Test func luminanceReadsHexColors() throws {
        #expect(PhotoMood.luminance("#000000") == 0)
        #expect(abs(try #require(PhotoMood.luminance("#FFFFFF")) - 1) < 0.001)
        #expect(PhotoMood.luminance("red") == nil)
    }

    @Test func photosThatSuitTheMoodComeFirstAndNoneAreDropped() {
        let ordered = PhotoMood.order([Fixtures.photo("bright", color: "#F2F2F2"), Fixtures.photo("dark", color: "#141414")], .dark)
        #expect(ordered.map(\.id) == ["dark", "bright"])
    }

    @Test func localDateFollowsTheCalendarsTimeZone() {
        let moment = Date.utc(2026, 10, 14, 22) // 03:00 the next day in Tashkent (UTC+5)
        #expect(FakeClock(moment).today == LocalDate(year: 2026, month: 10, day: 14))
        #expect(FakeClock(moment, timeZone: TimeZone(identifier: "Asia/Tashkent")!).today == LocalDate(year: 2026, month: 10, day: 15))
        #expect(LocalDate(string: "2026-01-05")?.description == "2026-01-05")
    }

    @Test func applyLockIsExclusive() throws {
        let dir = TempDir()
        let file = dir.paths.lockFile

        var first = ApplyLock.tryAcquire(file)
        #expect(first != nil)
        #expect(ApplyLock.tryAcquire(file) == nil)

        first = nil
        #expect(ApplyLock.tryAcquire(file) != nil)
    }
}
