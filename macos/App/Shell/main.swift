import AppKit

// The same executable is the app and the command line (launchd runs `Oriel --apply-today`).
MainActor.assumeIsolated {
    let arguments = Array(CommandLine.arguments.dropFirst())
    if Cli.isCli(arguments) { Cli.runAndExit(arguments) }

    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    // A menu bar app: no Dock icon until the window opens.
    app.setActivationPolicy(.accessory)
    withExtendedLifetime(delegate) { app.run() }
}
