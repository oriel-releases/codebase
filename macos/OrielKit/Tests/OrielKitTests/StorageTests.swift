import Foundation
import Testing
@testable import OrielKit

struct SettingsTests {
    let dir = TempDir()

    @Test func settingsRoundTripWithDefaults() {
        let store = JSONSettingsStore(paths: dir.paths)
        var s = store.load()
        #expect(s.enabledQueries().contains("nature"))
        #expect(s.dailyTime == "09:00")

        s.fit = .span
        s.topics.append(TopicSetting(query: "foggy forest", enabled: true, isCustom: true))
        store.save(s)

        let again = store.load()
        #expect(again.fit == .span)
        #expect(again.enabledQueries().contains("foggy forest"))
    }

    @Test func corruptSettingsFallBackToDefaults() throws {
        let paths = dir.paths
        try paths.ensureCreated()
        try Data("{ not json".utf8).write(to: paths.settingsFile)

        #expect(JSONSettingsStore(paths: paths).load().dailyTime == "09:00")
    }

    @Test func missingKeysTakeDefaultsAndWindowsTileBecomesFill() throws {
        let paths = dir.paths
        try paths.ensureCreated()
        try Data(#"{"dailyTime": "07:30", "fit": "Tile", "lockScreen": true}"#.utf8).write(to: paths.settingsFile)

        let s = JSONSettingsStore(paths: paths).load()
        #expect(s.dailyTime == "07:30")
        #expect(s.fit == .fill)
        #expect(s.matchTimeOfDay)
        #expect(s.enabledQueries().contains("mountains"))
    }

    @Test func stateRoundTrips() {
        let store = JSONSettingsStore(paths: dir.paths)
        store.updateState {
            $0.lastAppliedDate = LocalDate(year: 2026, month: 10, day: 14)
            $0.consecutiveFailures = 2
            $0.nextRetryAt = .utc(2026, 10, 14, 9)
        }

        let state = store.loadState()
        #expect(state.lastAppliedDate == LocalDate(year: 2026, month: 10, day: 14))
        #expect(state.consecutiveFailures == 2)
        #expect(state.nextRetryAt == .utc(2026, 10, 14, 9))
    }

    @Test func timestampsKeepEveryDigit() throws {
        let micro = try #require(ISO8601.parse("2026-10-20T16:30:00.123456Z"))
        let milli = try #require(ISO8601.parse("2026-10-20T16:30:00.123Z"))
        #expect(micro > milli)
        #expect(abs(micro.timeIntervalSince(milli) - 0.000456) < 0.000001)
        #expect(ISO8601.parse("2026-10-20T16:30:00Z") != nil)
        // A time read back writes the same text (not one millisecond less), so "since" queries don't repeat.
        for text in ["2026-10-15T18:45:21.539Z", "2026-10-15T18:45:21.001Z", "2026-10-15T18:45:21.999Z", "1969-12-31T23:59:59.500Z"] {
            #expect(ISO8601.format(try #require(ISO8601.parse(text))) == text)
        }
        #expect(ISO8601.parse("2026-10-20T21:30:00.5+05:00") == Date.utc(2026, 10, 20, 16).addingTimeInterval(30 * 60 + 0.5))
    }

    /// The syncs, the scheduler and the UI save the state at the same moments: no change may get lost.
    @Test func stateChangesFromManyThreadsAtOnceAllStay() async {
        let store = JSONSettingsStore(paths: dir.paths)
        await withTaskGroup(of: Void.self) { group in
            for i in 0..<40 {
                group.addTask { store.updateState { $0.manualChanges.append(Date(timeIntervalSince1970: Double(i))) } }
                group.addTask { store.updateState { $0.accentColor = "#00000\(i % 10)" } }
            }
        }
        #expect(store.loadState().manualChanges.count == 40)
        #expect(store.loadState().accentColor != nil)
    }

    @Test func savedInterestsFollowTheNewBuiltInList() {
        var s = OrielSettings()
        s.topics = [
            TopicSetting(query: "nature", enabled: true),
            TopicSetting(query: "minimal", enabled: true),   // no longer built in, but on: kept as your own
            TopicSetting(query: "abstract", enabled: false), // no longer built in, off: gone
            TopicSetting(query: "tokyo night", enabled: false, isCustom: true),
        ]

        s.updateBuiltInTopics()

        #expect(s.topics.filter { !$0.isCustom }.map(\.query) == OrielSettings.builtInTopics)
        #expect(s.topics.first { $0.query == "nature" }?.enabled == true)
        #expect(s.topics.first { $0.query == "waterfalls" }?.enabled == false) // new ones start off
        #expect(s.topics.first { $0.query == "minimal" }?.isCustom == true)
        #expect(!s.topics.contains { $0.query == "abstract" })
        #expect(s.topics.contains { $0.query == "tokyo night" && $0.isCustom })
    }

    @Test func enabledQueriesAreTrimmedAndUnique() {
        var s = OrielSettings()
        s.topics = [
            TopicSetting(query: " Fog ", enabled: true),
            TopicSetting(query: "fog", enabled: true, isCustom: true),
            TopicSetting(query: "snow", enabled: false),
            TopicSetting(query: "  ", enabled: true),
        ]
        #expect(s.enabledQueries() == ["Fog"])
    }

    @Test func badDailyTimeFallsBackToNine() {
        var s = OrielSettings()
        s.dailyTime = "25:99"
        #expect(s.dailyHourMinute == (9, 0))
        s.dailyTime = "18:45"
        #expect(s.dailyHourMinute == (18, 45))
    }
}

struct HistoryTests {
    let dir = TempDir()

    @Test func tracksFavoritesAndSoftDeletes() throws {
        let db = try SQLiteHistoryStore(paths: dir.paths)
        let id = try db.add(Fixtures.entry("a", at: Date(), file: URL(filePath: "/tmp/a.jpg")))

        db.setFavorite(id, true)
        #expect(db.get(id)?.isFavorite == true)

        db.markDeleted(id)
        #expect(db.list().isEmpty)
        #expect(db.list(includeDeleted: true).count == 1)
        #expect(db.wasUsed(provider: "unsplash", photoID: "a")) // still blocks reuse
        #expect(!db.wasUsed(provider: "unsplash", photoID: "b"))
    }

    @Test func addingAgainRevivesTheSameRow() throws {
        let db = try SQLiteHistoryStore(paths: dir.paths)
        let first = try db.add(Fixtures.entry("a", at: .utc(2026, 1, 1), file: URL(filePath: "/tmp/old.jpg")))
        db.markDeleted(first)

        let second = try db.add(Fixtures.entry("a", at: .utc(2026, 2, 1), file: URL(filePath: "/tmp/new.jpg")))

        #expect(second == first)
        let entry = try #require(db.get(first))
        #expect(entry.filePath == "/tmp/new.jpg")
        #expect(entry.appliedAt == .utc(2026, 2, 1))
        #expect(!entry.isDeleted)
    }

    @Test func listsNewestFirstAndKeepsOptionalFields() throws {
        let db = try SQLiteHistoryStore(paths: dir.paths)
        var older = Fixtures.entry("old", at: .utc(2026, 1, 1), file: URL(filePath: "/tmp/1.jpg"))
        older.topic = "fog"
        older.screenID = "SCREEN1"
        try db.add(older)
        try db.add(Fixtures.entry("new", at: .utc(2026, 3, 1), file: URL(filePath: "/tmp/2.jpg")))

        let list = db.list()
        #expect(list.map(\.photoID) == ["new", "old"])
        #expect(list[1].topic == "fog")
        #expect(list[1].screenID == "SCREEN1")
        #expect(list[0].description == nil)
    }

    @Test func updatesFilePathsTogether() throws {
        let db = try SQLiteHistoryStore(paths: dir.paths)
        let id = try db.add(Fixtures.entry("a", at: Date(), file: URL(filePath: "/old/a.jpg")))

        try db.updateFilePaths(["/old/a.jpg": "/new/a.jpg", "/old/missing.jpg": "/new/missing.jpg"])

        #expect(db.get(id)?.filePath == "/new/a.jpg")
    }

    @Test func retentionDeletesOldNonFavoritesOnly() throws {
        let paths = dir.paths
        let clock = FakeClock(.utc(2026, 10, 3, 9))
        let db = try SQLiteHistoryStore(paths: paths)
        let store = JSONSettingsStore(paths: paths)
        let setter = FakeSetter()
        let longAgo = clock.now.addingTimeInterval(-40 * 86_400)

        let old = try db.add(Fixtures.entry("old", at: longAgo, file: Fixtures.file("old.jpg", in: paths.picturesRoot)))
        let fav = try db.add(Fixtures.entry("fav", at: longAgo, file: Fixtures.file("fav.jpg", in: paths.picturesRoot)))
        let onScreen = try db.add(Fixtures.entry("screen", at: longAgo, file: Fixtures.file("screen.jpg", in: paths.picturesRoot)))
        let fresh = try db.add(Fixtures.entry("fresh", at: clock.now.addingTimeInterval(-2 * 86_400),
                                              file: Fixtures.file("fresh.jpg", in: paths.picturesRoot)))
        db.setFavorite(fav, true)
        try setter.setForAll(db.get(onScreen)!.fileURL, fit: .fill)

        let removed = RetentionService(history: db, settings: store, setter: setter, clock: clock).cleanup()

        func exists(_ id: Int64) -> Bool { FileManager.default.fileExists(atPath: db.get(id)!.filePath) }
        #expect(removed == 1)
        #expect(!exists(old))
        #expect(db.get(old)?.isDeleted == true)
        #expect(exists(fav))
        #expect(exists(onScreen))
        #expect(exists(fresh))
    }

    @Test func slideshowKeepsPhotosForAWeek() throws {
        let paths = dir.paths
        let clock = FakeClock(.utc(2026, 10, 3, 9))
        let db = try SQLiteHistoryStore(paths: paths)
        let store = JSONSettingsStore(paths: paths)
        store.update { $0.intervalMinutes = 30 }
        try db.add(Fixtures.entry("tenDays", at: clock.now.addingTimeInterval(-10 * 86_400),
                                  file: Fixtures.file("a.jpg", in: paths.picturesRoot)))

        let removed = RetentionService(history: db, settings: store, setter: FakeSetter(), clock: clock).cleanup()

        #expect(removed == 1)
    }
}
