import Foundation
import Synchronization

public struct SyncedInterest: Codable, Sendable, Hashable {
    public var query: String
    public var enabled: Bool
    public var isCustom: Bool
}

/// The settings every device of an account shares (`users/{uid}/sync/settings`). Device-only ones stay local: fit,
/// a photo per display, folder, open at login, change at login, shortcut, following, onboarding.
public struct SyncedSettings: Codable, Sendable, Hashable {
    public var dailyTime: String
    public var timeZone: String
    public var intervalMinutes: Int
    public var paused: Bool
    public var favoritesOnly: Bool
    public var skipLowLikes: Bool
    public var minLikes: Int
    public var interests: [SyncedInterest]
    public var matchTimeOfDay: Bool = true

    public static func from(_ s: OrielSettings) -> SyncedSettings {
        SyncedSettings(
            dailyTime: s.dailyTime, timeZone: s.timeZone, intervalMinutes: s.intervalMinutes, paused: s.paused,
            favoritesOnly: s.favoritesOnly, skipLowLikes: s.skipLowLikes, minLikes: s.minLikes,
            interests: s.topics.filter { !$0.query.trimmingCharacters(in: .whitespaces).isEmpty }
                .map { SyncedInterest(query: $0.query.trimmingCharacters(in: .whitespaces), enabled: $0.enabled, isCustom: $0.isCustom) },
            matchTimeOfDay: s.matchTimeOfDay)
    }

    /// Puts these into `s`, keeping what's only this device's (your own interests' cover photos).
    public func apply(to s: inout OrielSettings) {
        s.dailyTime = dailyTime
        s.timeZone = timeZone
        s.intervalMinutes = intervalMinutes
        s.paused = paused
        s.favoritesOnly = favoritesOnly
        s.skipLowLikes = skipLowLikes
        s.minLikes = minLikes
        s.matchTimeOfDay = matchTimeOfDay
        var covers: [String: String] = [:]
        for t in s.topics { if let cover = t.coverURL, covers[t.query.lowercased()] == nil { covers[t.query.lowercased()] = cover } }
        s.topics = interests.map {
            TopicSetting(query: $0.query, enabled: $0.enabled, isCustom: $0.isCustom, coverURL: covers[$0.query.lowercased()])
        }
        s.updateBuiltInTopics()
    }

    /// The schedule part: changing it starts the schedule fresh (no catch-up of a moment that just moved).
    public func sameSchedule(_ other: SyncedSettings) -> Bool {
        dailyTime == other.dailyTime && timeZone == other.timeZone && intervalMinutes == other.intervalMinutes && paused == other.paused
    }

    /// A stable text of the record, to tell a real change from an echo.
    public var fingerprint: String {
        let e = JSONEncoder()
        e.outputFormatting = .sortedKeys
        return String(decoding: (try? e.encode(self)) ?? Data(), as: UTF8.self)
    }
}

/// Keeps the shared settings the same on every device: a change here is written to `sync/settings` and rings the
/// other devices; a ring from another device, or opening the app, brings theirs here. Last write wins. A device
/// signing in for the first time adopts the account's settings, or uploads its own when the account has none yet.
public final class SettingsSync: Sendable {
    private let auth: AuthService
    private let firebase: FirebaseClient
    private let store: JSONSettingsStore
    private let gate = AsyncGate()
    private let last = Mutex<SyncedSettings?>(nil)
    private let debounce = Mutex<Task<Void, Never>?>(nil)

    public init(auth: AuthService, firebase: FirebaseClient, store: JSONSettingsStore) {
        self.auth = auth
        self.firebase = firebase
        self.store = store
    }

    public func start() {
        last.withLock { $0 = SyncedSettings.from(store.load()) }
        NotificationCenter.default.addObserver(forName: .orielSettingsChanged, object: store, queue: nil) { [weak self] _ in
            self?.localChange()
        }
        auth.changed.subscribe { [weak self] in
            guard let self, self.auth.isSignedIn else { return }
            Task { await self.pull() }
        }
        Task { await pull() }
    }

    /// A setting changed here (or a pull applied one): a schedule edit starts fresh, and a real change is uploaded.
    private func localChange() {
        let now = SyncedSettings.from(store.load())
        let before = last.withLock { old in
            defer { old = now }
            return old
        }
        if let before, !before.sameSchedule(now) {
            store.updateState { $0.scheduleStartedAt = Date() }
        }
        if now.fingerprint == store.loadState().syncedSettingsHash { return } // came from the account: nothing to send
        // A slider or several toggles in a row: one upload when it settles.
        debounce.withLock { task in
            task?.cancel()
            task = Task { [weak self] in
                try? await Task.sleep(for: .seconds(1.2))
                guard !Task.isCancelled else { return }
                await self?.push()
            }
        }
    }

    /// Uploads this device's shared settings and rings the others.
    public func push() async {
        guard auth.isSignedIn else { return }
        await gate.lock()
        defer { gate.unlock() }
        do {
            guard let tokens = try await auth.tokens() else { return }
            let mine = SyncedSettings.from(store.load())
            let device = auth.current?.sessionId ?? ""
            let at = Date()
            try await firebase.setSyncedSettings(tokens, mine, device: device, at: at)
            store.updateState {
                $0.syncedSettingsHash = mine.fingerprint
                $0.settingsSyncedAt = at
                $0.settingsDirty = false
            }
            Log.settingsSync.info("Shared your settings with your other devices")
            do {
                try await firebase.ringSettings(tokens, device: device)
            } catch {
                Log.settingsSync.info("Couldn't ring the other devices about settings: \(error.localizedDescription)")
            }
        } catch {
            // Offline: remembered, and sent the next time this device talks to the account.
            store.updateState { $0.settingsDirty = true }
            Log.settingsSync.info("Couldn't share your settings yet: \(error.localizedDescription)")
        }
    }

    /// Brings the account's settings here when they're newer (another device changed them).
    public func pull() async {
        guard auth.isSignedIn else { return }
        await gate.lock()
        var shouldPush = false
        do {
            shouldPush = try await pullCore()
        } catch {
            Log.settingsSync.info("Couldn't read the shared settings: \(error.localizedDescription)")
        }
        gate.unlock()
        if shouldPush { await push() }
    }

    /// Takes the account's settings when they're newer. True when this device's should be uploaded instead.
    private func pullCore() async throws -> Bool {
        guard let tokens = try await auth.tokens() else { return false }
        guard let (theirs, updatedAt) = try await firebase.syncedSettings(tokens) else {
            return true // the first device: the account takes this one's settings
        }
        var state = store.loadState()
        var settings = store.load()
        let mine = SyncedSettings.from(settings)
        if theirs.fingerprint == mine.fingerprint {
            state.syncedSettingsHash = mine.fingerprint
            state.settingsSyncedAt = updatedAt
            state.settingsDirty = false
            store.saveState(state)
            return false
        }
        // Changed here while offline, after the account's last change: this device's is the newer one.
        if state.settingsDirty, let synced = state.settingsSyncedAt, synced >= updatedAt { return true }
        theirs.apply(to: &settings)
        state.syncedSettingsHash = SyncedSettings.from(settings).fingerprint
        state.settingsSyncedAt = updatedAt
        state.settingsDirty = false
        store.saveState(state)
        store.save(settings) // localChange: a schedule change starts fresh, and nothing is sent back
        Log.settingsSync.info("Took the settings changed on another device")
        return false
    }
}

/// "First device wins" for scheduled changes: every device ticks at the same moments, and the first to claim a moment
/// (`users/{uid}/sync/slot`) picks the photo; the others show that one (it arrives through `sync/current`).
public struct SlotClaimer: SlotClaiming {
    let auth: AuthService
    let firebase: FirebaseClient

    public init(auth: AuthService, firebase: FirebaseClient) {
        self.auth = auth
        self.firebase = firebase
    }

    public func tryClaim(_ slotId: String) async -> Bool? {
        do {
            guard auth.isSignedIn, let tokens = try await auth.tokens() else { return nil }
            let won = try await firebase.claimSlot(tokens, slotId: slotId, device: auth.current?.sessionId ?? "")
            Log.slots.info(won ? "This device takes the \(slotId) change" : "Another device already took the \(slotId) change")
            return won
        } catch {
            Log.slots.info("Couldn't claim the \(slotId) change (\(error.localizedDescription)); changing anyway")
            return nil
        }
    }
}
