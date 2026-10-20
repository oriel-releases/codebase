import Foundation

public struct TopicSetting: Sendable, Hashable, Codable {
    public var query: String
    public var enabled: Bool
    public var isCustom: Bool
    /// Your own interests: the photo its tile shows, found once and kept (Unsplash thumbnail link).
    public var coverURL: String?

    public init(query: String, enabled: Bool, isCustom: Bool = false, coverURL: String? = nil) {
        self.query = query
        self.enabled = enabled
        self.isCustom = isCustom
        self.coverURL = coverURL
    }

    enum CodingKeys: String, CodingKey {
        case query, enabled, isCustom
        case coverURL = "coverUrl"
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        query = try c.decodeIfPresent(String.self, forKey: .query) ?? ""
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? false
        isCustom = try c.decodeIfPresent(Bool.self, forKey: .isCustom) ?? false
        coverURL = try c.decodeIfPresent(String.self, forKey: .coverURL)
    }
}

/// What the user chose. Saved as settings.json with the same keys as the Windows app, so the account sync
/// (phase 2) speaks one format. Every key is optional when reading: a file from an older version keeps working.
public struct OrielSettings: Sendable, Hashable, Codable {
    /// The interests every install offers. Same list as Windows.
    public static let builtInTopics = [
        "nature", "mountains", "ocean", "beach", "lakes", "waterfalls", "forest", "autumn",
        "flowers", "desert", "snow", "northern lights", "night sky", "space", "sunset", "cities",
        "city at night", "architecture", "japan", "islands", "wildlife", "countryside", "aerial", "fog",
    ]

    static let defaultEnabled: Set<String> = ["nature", "mountains", "ocean", "space", "architecture"]

    public var topics: [TopicSetting] = OrielSettings.builtInTopics.map {
        TopicSetting(query: $0, enabled: OrielSettings.defaultEnabled.contains($0))
    }

    /// Time of day for the daily change, "HH:mm", in `timeZone`.
    public var dailyTime = "09:00"
    /// The IANA time zone the schedule is anchored in (like "Asia/Tashkent"). Starts as this Mac's own.
    public var timeZone = TimeZone.current.identifier
    /// Slideshow: minutes between changes. 0 means once a day at `dailyTime`.
    public var intervalMinutes = 0
    /// Pick from the photos starred in History instead of fetching new ones.
    public var favoritesOnly = false
    /// Also change at login when today's photo isn't set yet.
    public var applyAtLogin = true
    public var paused = false

    public var fit = WallpaperFit.fill

    public var skipLowLikes = false
    public var minLikes = 50

    /// Where photos are saved; nil means the default (~/Pictures/Oriel).
    public var wallpaperFolder: String?

    /// Start Oriel in the menu bar when you log in (a login item). Windows: StartWithWindows.
    public var startAtLogin = true

    /// Prefer darker photos in the evening (and in dark mode), brighter ones in the morning.
    public var matchTimeOfDay = true

    /// Change the Mac's accent color to the closest one to each new wallpaper's color. This Mac only, off by default.
    public var matchAccentColor = false

    /// With more than one screen, give each its own photo instead of the same one everywhere.
    public var photoPerMonitor = false

    /// Following a friend: their account, first name and Google picture. While set, the own schedule is off and the
    /// friend's wallpaper is used. Per device.
    public var followUid: String?
    public var followName: String?
    public var followPicture: String?

    /// This device's code for friends to follow (nil while not sharing).
    public var myFollowCode: String?

    /// The first-run tour was finished or skipped.
    public var onboardingDone = false

    /// Global shortcut for "next wallpaper", stored with Windows-style names ("Ctrl+Alt+W", Alt = Option). Empty: none.
    public var hotkey = "Ctrl+Alt+W"

    public init() {}

    public var isFollowing: Bool { !(followUid ?? "").isEmpty }

    public var zone: TimeZone { TimeZone(identifier: timeZone) ?? .current }

    /// `dailyTime` as hour and minute; 09:00 when it can't be read.
    public var dailyHourMinute: (hour: Int, minute: Int) {
        let parts = dailyTime.split(separator: ":").compactMap { Int($0) }
        guard parts.count == 2, (0..<24).contains(parts[0]), (0..<60).contains(parts[1]) else { return (9, 0) }
        return (parts[0], parts[1])
    }

    /// Seconds between slideshow changes, or nil for once a day.
    public var interval: TimeInterval? { intervalMinutes > 0 ? TimeInterval(intervalMinutes * 60) : nil }

    /// Brings a saved list up to date with `builtInTopics`: new ones are added (off), ones no longer built in go,
    /// except that one you had on stays, as one of your own. Your own and your on/off choices are kept.
    public mutating func updateBuiltInTopics() {
        let saved = topics.filter { !$0.query.trimmingCharacters(in: .whitespaces).isEmpty }
        // A brand-new install (nothing saved yet) keeps its defaults.
        guard !saved.isEmpty else { return }
        let builtIn = Set(Self.builtInTopics.map { $0.lowercased() })

        var kept = Self.builtInTopics.map { name in
            let mine = saved.first { !$0.isCustom && $0.query.caseInsensitiveCompare(name) == .orderedSame }
            return TopicSetting(query: name, enabled: mine?.enabled ?? false)
        }
        for t in saved {
            if t.isCustom {
                kept.append(t)
            } else if !builtIn.contains(t.query.lowercased()) && t.enabled {
                kept.append(TopicSetting(query: t.query, enabled: true, isCustom: true))
            }
        }
        topics = kept
    }

    /// The topics to pick from: enabled, trimmed, without duplicates (ignoring case).
    public func enabledQueries() -> [String] {
        var seen = Set<String>()
        return topics.compactMap { t in
            let q = t.query.trimmingCharacters(in: .whitespaces)
            guard t.enabled, !q.isEmpty, seen.insert(q.lowercased()).inserted else { return nil }
            return q
        }
    }

    enum CodingKeys: String, CodingKey {
        case topics, dailyTime, timeZone, intervalMinutes, favoritesOnly, applyAtLogin, paused, fit, skipLowLikes,
             minLikes, wallpaperFolder, startAtLogin, matchTimeOfDay, matchAccentColor, photoPerMonitor, followUid, followName,
             followPicture, myFollowCode, onboardingDone, hotkey
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = OrielSettings()
        topics = try c.decodeIfPresent([TopicSetting].self, forKey: .topics) ?? d.topics
        dailyTime = try c.decodeIfPresent(String.self, forKey: .dailyTime) ?? d.dailyTime
        timeZone = try c.decodeIfPresent(String.self, forKey: .timeZone) ?? d.timeZone
        intervalMinutes = try c.decodeIfPresent(Int.self, forKey: .intervalMinutes) ?? d.intervalMinutes
        favoritesOnly = try c.decodeIfPresent(Bool.self, forKey: .favoritesOnly) ?? d.favoritesOnly
        applyAtLogin = try c.decodeIfPresent(Bool.self, forKey: .applyAtLogin) ?? d.applyAtLogin
        paused = try c.decodeIfPresent(Bool.self, forKey: .paused) ?? d.paused
        fit = try c.decodeIfPresent(WallpaperFit.self, forKey: .fit) ?? d.fit
        skipLowLikes = try c.decodeIfPresent(Bool.self, forKey: .skipLowLikes) ?? d.skipLowLikes
        minLikes = try c.decodeIfPresent(Int.self, forKey: .minLikes) ?? d.minLikes
        wallpaperFolder = try c.decodeIfPresent(String.self, forKey: .wallpaperFolder)
        startAtLogin = try c.decodeIfPresent(Bool.self, forKey: .startAtLogin) ?? d.startAtLogin
        matchTimeOfDay = try c.decodeIfPresent(Bool.self, forKey: .matchTimeOfDay) ?? d.matchTimeOfDay
        matchAccentColor = try c.decodeIfPresent(Bool.self, forKey: .matchAccentColor) ?? d.matchAccentColor
        photoPerMonitor = try c.decodeIfPresent(Bool.self, forKey: .photoPerMonitor) ?? d.photoPerMonitor
        followUid = try c.decodeIfPresent(String.self, forKey: .followUid)
        followName = try c.decodeIfPresent(String.self, forKey: .followName)
        followPicture = try c.decodeIfPresent(String.self, forKey: .followPicture)
        myFollowCode = try c.decodeIfPresent(String.self, forKey: .myFollowCode)
        onboardingDone = try c.decodeIfPresent(Bool.self, forKey: .onboardingDone) ?? d.onboardingDone
        hotkey = try c.decodeIfPresent(String.self, forKey: .hotkey) ?? d.hotkey
    }
}

/// Runtime state that is not user-editable (state.json).
public struct OrielState: Sendable, Hashable, Codable {
    public var lastAppliedDate: LocalDate?
    public var lastAppliedAt: Date?
    public var lastAttemptAt: Date?
    public var lastError: String?
    public var consecutiveFailures = 0
    public var nextRetryAt: Date?
    /// When "Change now" last succeeded, for its per-hour limit. Only the last hour matters.
    public var manualChanges: [Date] = []
    /// When the schedule last started fresh (it was edited). Ticks before it aren't "missed", so an edit never
    /// changes the wallpaper by itself.
    public var scheduleStartedAt: Date?
    /// The shared settings as last sent or received (to tell a real change from an echo), and when.
    public var syncedSettingsHash: String?
    public var settingsSyncedAt: Date?
    /// Changed here but not sent yet (offline).
    public var settingsDirty = false
    /// The newest favorite change taken from the account (the next sync asks only for later ones).
    public var favoritesSyncedAt: Date?
    /// The account's (or followed friend's) photo already applied here, so it isn't set again.
    public var followedPhotoId: String?
    /// Why following doesn't work right now (the friend stopped sharing), for the UI.
    public var followProblem: String?
    /// The accent color chosen for the photo on screen (here or on another device), "#RRGGBB". Only the photo on
    /// screen matters, so one pair is kept.
    public var accentPhotoId: String?
    public var accentColor: String?

    public init() {}

    enum CodingKeys: String, CodingKey {
        case lastAppliedDate, lastAppliedAt, lastAttemptAt, lastError, consecutiveFailures, nextRetryAt,
             manualChanges, scheduleStartedAt, syncedSettingsHash, settingsSyncedAt, settingsDirty, favoritesSyncedAt,
             followedPhotoId, followProblem, accentPhotoId, accentColor
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        lastAppliedDate = try c.decodeIfPresent(LocalDate.self, forKey: .lastAppliedDate)
        lastAppliedAt = try c.decodeIfPresent(Date.self, forKey: .lastAppliedAt)
        lastAttemptAt = try c.decodeIfPresent(Date.self, forKey: .lastAttemptAt)
        lastError = try c.decodeIfPresent(String.self, forKey: .lastError)
        consecutiveFailures = try c.decodeIfPresent(Int.self, forKey: .consecutiveFailures) ?? 0
        nextRetryAt = try c.decodeIfPresent(Date.self, forKey: .nextRetryAt)
        manualChanges = try c.decodeIfPresent([Date].self, forKey: .manualChanges) ?? []
        scheduleStartedAt = try c.decodeIfPresent(Date.self, forKey: .scheduleStartedAt)
        syncedSettingsHash = try c.decodeIfPresent(String.self, forKey: .syncedSettingsHash)
        settingsSyncedAt = try c.decodeIfPresent(Date.self, forKey: .settingsSyncedAt)
        settingsDirty = try c.decodeIfPresent(Bool.self, forKey: .settingsDirty) ?? false
        favoritesSyncedAt = try c.decodeIfPresent(Date.self, forKey: .favoritesSyncedAt)
        followedPhotoId = try c.decodeIfPresent(String.self, forKey: .followedPhotoId)
        followProblem = try c.decodeIfPresent(String.self, forKey: .followProblem)
        accentPhotoId = try c.decodeIfPresent(String.self, forKey: .accentPhotoId)
        accentColor = try c.decodeIfPresent(String.self, forKey: .accentColor)
    }
}
