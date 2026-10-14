import Foundation
import Synchronization

/// The background fallback (Windows: Task Scheduler): a launchd agent that runs `Oriel --apply-today` at the daily time
/// (or every interval), so the change happens even when the app is closed. A failed run lines up a one-shot retry.
public protocol ScheduledTasks: Sendable {
    func register(executable: String, hour: Int, minute: Int, runAtLoad: Bool, interval: TimeInterval?)
    func scheduleRetry(executable: String, at: Date)
    func unregister()
}

/// Opening Oriel at login (Windows: the Run key). The app implements it with SMAppService.
public protocol StartupRegistration: Sendable {
    func set(_ enabled: Bool)
}

public struct LaunchAgents: ScheduledTasks {
    public static let applyLabel = "uz.akbarjon.oriel.apply"
    public static let retryLabel = "uz.akbarjon.oriel.retry"

    let folder: URL

    public init(folder: URL = FileManager.default.homeDirectoryForCurrentUser.appending(path: "Library/LaunchAgents", directoryHint: .isDirectory)) {
        self.folder = folder
    }

    func plist(_ label: String) -> URL { folder.appending(path: "\(label).plist") }

    public func register(executable: String, hour: Int, minute: Int, runAtLoad: Bool, interval: TimeInterval?) {
        var job: [String: Any] = [
            "Label": Self.applyLabel,
            "ProgramArguments": [executable, "--apply-today"],
            // Catch up at login only when the app itself doesn't start then.
            "RunAtLoad": runAtLoad,
            "ProcessType": "Background",
            "LowPriorityIO": true,
            "Nice": 10,
        ]
        // The run itself decides with isDue, so the timing only has to be roughly right. launchd runs a missed
        // calendar job after wake, like Task Scheduler's StartWhenAvailable.
        if let interval {
            job["StartInterval"] = Int(interval)
        } else {
            job["StartCalendarInterval"] = ["Hour": hour, "Minute": minute]
        }
        install(Self.applyLabel, job)
    }

    public func scheduleRetry(executable: String, at: Date) {
        let c = Calendar.current.dateComponents([.month, .day, .hour, .minute], from: at)
        install(Self.retryLabel, [
            "Label": Self.retryLabel,
            "ProgramArguments": [executable, "--apply-today"],
            "StartCalendarInterval": ["Month": c.month!, "Day": c.day!, "Hour": c.hour!, "Minute": c.minute!],
            "ProcessType": "Background",
            "LowPriorityIO": true,
            "Nice": 10,
        ])
    }

    /// The retry agent removes itself when its run starts.
    public func removeRetry() {
        remove(Self.retryLabel)
    }

    public func unregister() {
        remove(Self.applyLabel)
        remove(Self.retryLabel)
    }

    private func install(_ label: String, _ job: [String: Any]) {
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let data = try PropertyListSerialization.data(fromPropertyList: job, format: .xml, options: 0)
            try data.write(to: plist(label), options: .atomic)
            Self.launchctl(["bootout", "gui/\(getuid())/\(label)"])
            let ok = Self.launchctl(["bootstrap", "gui/\(getuid())", plist(label).path(percentEncoded: false)])
            Log.launchd.info(ok ? "Registered \(label)" : "launchctl bootstrap \(label) failed")
        } catch {
            Log.launchd.warning("Couldn't register \(label)", error)
        }
    }

    private func remove(_ label: String) {
        Self.launchctl(["bootout", "gui/\(getuid())/\(label)"])
        try? FileManager.default.removeItem(at: plist(label))
    }

    @discardableResult
    static func launchctl(_ args: [String]) -> Bool {
        let p = Process()
        p.executableURL = URL(filePath: "/bin/launchctl")
        p.arguments = args
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        do {
            try p.run()
            p.waitUntilExit()
            return p.terminationStatus == 0
        } catch {
            return false
        }
    }
}

/// Keeps "Open at login" and the launchd fallback in line with the settings. Only touches them when a relevant setting
/// actually changes; at app start it re-registers so the agent points at the bundle that's running now.
public final class ScheduleSync: Sendable {
    private let store: SettingsStore
    private let startup: StartupRegistration
    private let agents: ScheduledTasks
    private let synced = Mutex<String?>(nil)

    public init(store: SettingsStore, startup: StartupRegistration, agents: ScheduledTasks) {
        self.store = store
        self.startup = startup
        self.agents = agents
    }

    public func sync(executable: String, force: Bool = false) {
        let s = store.load()
        let wanted = "\(s.dailyTime)|\(s.timeZone)|\(s.applyAtLogin)|\(s.paused)|\(s.startAtLogin)|\(s.intervalMinutes)"
        let changed = synced.withLock { last -> Bool in
            guard force || last != wanted else { return false }
            last = wanted
            return true
        }
        guard changed else { return }

        startup.set(s.startAtLogin)
        // Paused: no agent at all, so nothing runs.
        if s.paused {
            agents.unregister()
        } else {
            // launchd uses this Mac's local time; the schedule is in the shared time zone.
            let tick = ScheduleRules.nextTick(s, Date())
            let local = Calendar.current.dateComponents([.hour, .minute], from: tick)
            agents.register(executable: executable, hour: local.hour ?? 9, minute: local.minute ?? 0,
                            runAtLoad: s.applyAtLogin && !s.startAtLogin, interval: s.interval)
        }
    }
}
