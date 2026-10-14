import AppKit
import OrielKit

/// Headless mode: a short-lived process that does one thing and exits. launchd runs `--apply-today`; it's also
/// handy for scripting: `/Applications/Oriel.app/Contents/MacOS/Oriel --status`.
enum Cli {
    static let commands = ["--apply-today", "--change-now", "--status", "--help"]

    static func isCli(_ args: [String]) -> Bool {
        args.contains { commands.contains($0.lowercased()) }
    }

    /// Runs the command and exits the process with its code.
    @MainActor
    static func runAndExit(_ args: [String]) -> Never {
        // Wallpapers are set through AppKit: make the app object, but never show anything.
        NSApplication.shared.setActivationPolicy(.prohibited)
        let command = args.first { commands.contains($0.lowercased()) }!.lowercased()
        Task {
            let code: Int32
            switch command {
            case "--apply-today": code = await apply(background(), force: false)
            case "--change-now": code = await apply(background(), force: true)
            case "--status": code = status(AppHost.build())
            default: code = help()
            }
            exit(code)
        }
        // Keep the main thread serving AppKit (setting the wallpaper happens there) until the task exits.
        RunLoop.main.run()
        exit(0)
    }

    /// Services for a low-priority run. A run started by the retry agent removes that agent.
    private static func background() -> Services {
        setpriority(PRIO_PROCESS, 0, 10)
        let services = AppHost.build()
        services.agents.removeRetry()
        return services
    }

    private static func apply(_ sp: Services, force: Bool) async -> Int32 {
        let run = ScheduledRun(wallpapers: sp.wallpapers, retention: sp.retention, agents: sp.agents, store: sp.store)
        let result = await run.run(force: force, executable: AppHost.executable)
        // Download tracking and publishing to the other devices: done before exiting.
        await sp.wallpapers.afterChangeDone()
        switch result.status {
        case .applied:
            for e in result.entries { out("Applied '\(e.topic ?? "")' photo by \(e.photographerName): \(e.filePath)") }
            return 0
        case .alreadyAppliedToday:
            out("Today's wallpaper is already set.")
            return 0
        case .paused, .takenByOtherDevice:
            out(result.status == .paused ? "Oriel is paused." : "Another device took this change.")
            return 0
        case .busy, .rateLimited:
            out(result.error ?? "")
            return 0
        case .signInRequired:
            out("Sign in to Oriel first: open the app and sign in.")
            return 3
        case .failed:
            out("Failed: \(result.error ?? "") (current wallpaper kept)")
            return result.errorKind == .missingKey || result.errorKind == .unauthorized ? 3 : 1
        }
    }

    private static func status(_ sp: Services) -> Int32 {
        let s = sp.store.load()
        let st = sp.store.loadState()
        out("Built-in key: \(sp.provider.isConfigured)")
        out("Signed in:    \(sp.auth.isSignedIn)")
        out("Daily time:   \(s.dailyTime)   at login: \(s.applyAtLogin)   paused: \(s.paused)")
        out("Topics:       \(s.enabledQueries().joined(separator: ", "))")
        out("Last applied: \(st.lastAppliedDate?.description ?? "never")")
        for screen in sp.setter.screens() {
            out("Display:      \(screen.frame.width)x\(screen.frame.height)\(screen.isMain ? " primary" : "")")
        }
        out("Wallpaper:    \(sp.setter.current(on: nil)?.path(percentEncoded: false) ?? "(differs per display)")")
        if let error = st.lastError {
            out("Last error:   \(error)  (next retry \(st.nextRetryAt?.formatted(date: .omitted, time: .shortened) ?? "-"))")
        }
        return 0
    }

    private static func help() -> Int32 {
        out("""
            Oriel - daily wallpaper
              --apply-today   change wallpaper if today's isn't set yet (used by launchd)
              --change-now    change wallpaper right now
              --status        show current settings and last run
              --background    start the app in the menu bar
            """)
        return 0
    }

    private static func out(_ line: String) {
        print(line)
        fflush(stdout)
    }
}
