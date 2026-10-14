import Foundation
import Synchronization

/// Today's photo from the website's Wallpaper of the day.
public struct TodayPhoto: Sendable, Hashable {
    public var id: String
    public var path: String
    public var by: String
    public var handle: String

    public func thumbnailURL(width: Int) -> URL {
        URL(string: "https://images.unsplash.com/\(path)?w=\(width)&q=75&fm=jpg&auto=format&fit=crop")!
    }

    public var authorURL: URL {
        URL(string: "https://unsplash.com/@\(handle)?utm_source=oriel&utm_medium=referral")!
    }
}

/// Wallpaper of the day, the same photo as oriel.akbarjon.uz/today. The website publishes the list (`/today.json`);
/// the pick is the same rule as on the page: days since 1 January 1970 in local time, as a position in the list.
/// The list is kept on disk and fetched again every few days.
public final class TodayService: Sendable {
    static let refreshAfter: TimeInterval = 3 * 86_400

    private let http: HTTPClient
    private let paths: AppPaths
    private let clock: OrielClock
    private let cache = Mutex<(list: [TodayPhoto], loadedAt: Date)?>(nil)

    public init(http: HTTPClient = URLSession.oriel, paths: AppPaths, clock: OrielClock = SystemClock()) {
        self.http = http
        self.paths = paths
        self.clock = clock
    }

    /// Today's photo, or nil when the list can't be had (offline on the very first try).
    public func today() async -> TodayPhoto? {
        let list = await load()
        return list.isEmpty ? nil : Self.pick(list, on: clock.now, calendar: clock.calendar)
    }

    /// The rule shared with the website: whole days since 1 January 1970 (local), going round the list.
    public static func pick<T>(_ list: [T], on date: Date, calendar: Calendar) -> T {
        let local = calendar.dateComponents([.year, .month, .day], from: date)
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = .gmt
        let midnight = utc.date(from: local)!
        let day = Int((midnight.timeIntervalSince1970 / 86_400).rounded(.down))
        let n = list.count
        return list[((day % n) + n) % n]
    }

    private func load() async -> [TodayPhoto] {
        if let c = cache.withLock({ $0 }), !c.list.isEmpty, Date().timeIntervalSince(c.loadedAt) < Self.refreshAfter { return c.list }
        let file = paths.todayFile
        let modified = (try? FileManager.default.attributesOfItem(atPath: file.path(percentEncoded: false))[.modificationDate]) as? Date
        if modified.map({ Date().timeIntervalSince($0) >= Self.refreshAfter }) ?? true {
            do {
                let (data, response) = try await http.send(URLRequest(url: URL(string: OrielLinks.todayList)!))
                if (200..<300).contains(response.statusCode), !Self.parse(data).isEmpty { try data.write(to: file, options: .atomic) }
            } catch {
                Log.today.info("Couldn't get the Wallpaper of the day list: \(error.localizedDescription)")
            }
        }
        let list = (try? Data(contentsOf: file)).map(Self.parse) ?? []
        cache.withLock { $0 = (list, Date()) }
        return list
    }

    static func parse(_ data: Data) -> [TodayPhoto] {
        guard let o = try? JSONSerialization.jsonObject(with: data) as? [String: Any], let photos = o["photos"] as? [[String: Any]] else {
            return []
        }
        return photos.map {
            TodayPhoto(id: $0["id"] as? String ?? "", path: $0["path"] as? String ?? "", by: $0["by"] as? String ?? "",
                       handle: $0["handle"] as? String ?? "")
        }
        .filter { !$0.id.isEmpty && !$0.path.isEmpty }
    }
}

/// What this device has already counted, kept in `stats.json`.
public struct StatsMemory: Codable, Sendable, Equatable {
    /// The first day this device was seen (for 7 and 30 day retention), "yyyy-MM-dd" UTC.
    public var firstDay: LocalDate?
    /// The last day it was counted as active, so it is counted once a day at most.
    public var activeDay: LocalDate?
    public var retained7 = false
    public var retained30 = false

    public init() {}

    /// The counts for being open today, and what to remember after they were sent. `installedOn` is set for an
    /// existing install seen for the first time (so it isn't a "new" one).
    public static func forActiveDay(_ m: StatsMemory, today: LocalDate, platform: String, installedOn: LocalDate?) -> (fields: [String], next: StatsMemory)? {
        if m.activeDay == today { return nil }
        var next = m
        next.activeDay = today
        var fields = ["\(platform)_active"]
        if next.firstDay == nil {
            if let since = installedOn, since < today {
                next.firstDay = since
            } else {
                next.firstDay = today
                fields.append("\(platform)_new")
            }
        }
        let age = days(from: next.firstDay!, to: today)
        if age >= 7 && !next.retained7 {
            fields.append("retained7")
            next.retained7 = true
        }
        if age >= 30 && !next.retained30 {
            fields.append("retained30")
            next.retained30 = true
        }
        return (fields, next)
    }

    static func days(from a: LocalDate, to b: LocalDate) -> Int {
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = .gmt
        let da = utc.date(from: DateComponents(year: a.year, month: a.month, day: a.day))!
        let db = utc.date(from: DateComponents(year: b.year, month: b.month, day: b.day))!
        return utc.dateComponents([.day], from: da, to: db).day ?? 0
    }
}

/// Anonymous daily counts: new installs, active devices, devices still around after 7 and 30 days, and shares sent,
/// opened and set. One small document per day (`stats/2026-10-14`) holding numbers only: no user IDs.
public final class StatsReporter: Sendable {
    static let platform = "macos"
    public static let sharesSent = "sharesSent"
    public static let sharesOpened = "sharesOpened"
    public static let sharesSet = "sharesSet"

    private let auth: AuthService
    private let firebase: FirebaseClient
    private let history: HistoryStore
    private let paths: AppPaths
    private let gate = AsyncGate()

    public init(auth: AuthService, firebase: FirebaseClient, history: HistoryStore, paths: AppPaths) {
        self.auth = auth
        self.firebase = firebase
        self.history = history
        self.paths = paths
    }

    /// Counts a moment worth knowing about (a share sent, opened or set). Best effort, never in the way.
    public func count(_ field: String) {
        Task { _ = await send([field]) }
    }

    /// Counts this device as active today (once a day).
    public func markActive() async {
        guard auth.isSignedIn, gate.tryLock() else { return }
        defer { gate.unlock() }
        let today = Self.utcToday()
        let memory = load()
        var installedOn: LocalDate?
        if memory.firstDay == nil, let first = history.list(includeDeleted: true).min(by: { $0.appliedAt < $1.appliedAt }) {
            installedOn = Self.utcDay(first.appliedAt)
        }
        guard let step = StatsMemory.forActiveDay(memory, today: today, platform: Self.platform, installedOn: installedOn) else { return }
        if await send(step.fields) { save(step.next) }
    }

    private func send(_ fields: [String]) async -> Bool {
        do {
            return try await withTimeout(8) { [self] in
                guard let tokens = try await auth.tokens() else { return false }
                try await firebase.incrementStats(tokens, day: Self.utcToday().description, fields: fields)
                return true
            }
        } catch {
            Log.stats.debug("Couldn't send the daily counts: \(error.localizedDescription)")
            return false
        }
    }

    static func utcDay(_ date: Date) -> LocalDate {
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = .gmt
        return LocalDate(date, calendar: utc)
    }

    static func utcToday() -> LocalDate { utcDay(Date()) }

    private func load() -> StatsMemory {
        guard let data = try? Data(contentsOf: paths.statsFile) else { return StatsMemory() }
        return (try? JSONDecoder().decode(StatsMemory.self, from: data)) ?? StatsMemory()
    }

    private func save(_ memory: StatsMemory) {
        try? JSONEncoder().encode(memory).write(to: paths.statsFile, options: .atomic)
    }
}
