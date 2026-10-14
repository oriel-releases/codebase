import Foundation
import Synchronization

/// In-app scheduler. Sleeps until the next due time (at most 10 minutes, which copes with sleep/wake and clock
/// changes) and wakes early when settings change or the account signs in or out. Idle in between.
public final class DailyScheduler: Sendable {
    private let wallpapers: WallpaperService
    private let retention: RetentionService
    private let store: SettingsStore
    private let clock: OrielClock
    private let auth: AuthState?
    private let follow: FollowService?
    private let stats: StatsReporter?
    private let pulse: PulseListener?
    private let state = Mutex<(task: Task<Void, Never>?, wake: CheckedContinuation<Void, Never>?, lastCleanup: LocalDate?)>((nil, nil, nil))

    /// After every scheduled attempt (success or failure) so the UI can update.
    public let attempted = Broadcaster<ApplyResult>()

    public init(wallpapers: WallpaperService, retention: RetentionService, store: SettingsStore, clock: OrielClock = SystemClock(),
                auth: AuthState? = nil, follow: FollowService? = nil, stats: StatsReporter? = nil, pulse: PulseListener? = nil) {
        self.wallpapers = wallpapers
        self.retention = retention
        self.store = store
        self.clock = clock
        self.auth = auth
        self.follow = follow
        self.stats = stats
        self.pulse = pulse
    }

    public func start() {
        state.withLock { s in
            guard s.task == nil else { return }
            s.task = Task { [weak self] in await self?.run() }
        }
    }

    /// Checks again now (settings changed, signed in or out, woke from sleep, the clock changed).
    public func wake() {
        let waiting = state.withLock { s -> CheckedContinuation<Void, Never>? in
            defer { s.wake = nil }
            return s.wake
        }
        waiting?.resume()
    }

    private func run() async {
        var first = true
        while !Task.isCancelled {
            var wait = ScheduleRules.maxWait
            await check(isStartup: first)
            if auth?.isSignedIn != false {
                let s = store.load()
                wait = ScheduleRules.nextCheckIn(s, store.loadState(), clock.now)
                // Signed in: also wake up to pick up a wallpaper another device set. The live connection brings
                // changes at once; then a rare poll is only a safety net. Also while locked: a photo set by hand
                // elsewhere still shows everywhere.
                if follow != nil {
                    let poll = pulse?.isConnected == true ? ScheduleRules.maxWait : ScheduleRules.syncInterval
                    wait = min(wait, poll)
                }
            }
            first = false
            await sleep(wait)
        }
    }

    /// Sleeps for `seconds`, or until `wake()`.
    private func sleep(_ seconds: TimeInterval) async {
        await withCheckedContinuation { (c: CheckedContinuation<Void, Never>) in
            state.withLock { $0.wake = c }
            Task { [weak self] in
                try? await Task.sleep(for: .seconds(seconds))
                self?.wake()
            }
        }
    }

    private func check(isStartup: Bool) async {
        let today = clock.today
        if state.withLock({ $0.lastCleanup }) != today {
            retention.cleanup()
            state.withLock { $0.lastCleanup = today }
        }

        if auth?.isSignedIn == false { return }
        await stats?.markActive()
        // The account's photo first (or the followed friend's): when another device already changed it, for this very
        // tick too, that one shows here and no second photo is picked.
        await follow?.check()
        if store.load().isFollowing { return }
        // Missed while off and "change at login" is off: wait for the next tick instead of catching up later.
        if isStartup && !store.load().applyAtLogin && !ScheduleRules.isFresh(store.load(), store.loadState(), clock.now) {
            store.updateState { $0.scheduleStartedAt = clock.now }
            return
        }
        guard ScheduleRules.isDue(store.load(), store.loadState(), clock.now, isStartupCheck: isStartup) else { return }

        Log.scheduler.info("Scheduled change is due (startup=\(isStartup))")
        let result = await wallpapers.apply(force: false)
        // Another device took this moment: its photo arrives through sync/current.
        if result.status == .takenByOtherDevice { await follow?.check() }
        attempted.send(result)
    }
}

/// What one launchd run does: change the wallpaper if needed, tidy up, line up a retry on failure.
public struct ScheduledRun: Sendable {
    let wallpapers: WallpaperService
    let retention: RetentionService
    let agents: ScheduledTasks
    let store: SettingsStore

    public init(wallpapers: WallpaperService, retention: RetentionService, agents: ScheduledTasks, store: SettingsStore) {
        self.wallpapers = wallpapers
        self.retention = retention
        self.agents = agents
        self.store = store
    }

    public func run(force: Bool, executable: String) async -> ApplyResult {
        let result = await wallpapers.apply(force: force)
        if result.status == .applied {
            retention.cleanup()
        } else if result.isTransientFailure, let at = store.loadState().nextRetryAt {
            agents.scheduleRetry(executable: executable, at: at)
        }
        return result
    }
}
