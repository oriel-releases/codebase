import SwiftUI
import OrielKit

/// Lives in the menu bar. The main window comes and goes; the Dock icon shows only while it's open.
@main
struct OrielApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        MenuBarExtra {
            MenuBarPanel()
                .environment(appDelegate.model)
        } label: {
            Image(systemName: appDelegate.model.settings.paused ? "photo.badge.exclamationmark" : "photo.on.rectangle.angled")
        }
        .menuBarExtraStyle(.window)
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    let model = AppModel()
    private var window: NSWindow?

    func applicationDidFinishLaunching(_ notification: Notification) {
        model.showWindow = { [weak self] in self?.showWindow() }
        model.start()
        // First run: say hello. After that Oriel starts quietly in the menu bar.
        if !model.settings.onboardingDone {
            model.settings.onboardingDone = true
            showWindow()
        }
    }

    /// Opening Oriel again (Finder, Spotlight, Dock) shows the window.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows: Bool) -> Bool {
        showWindow()
        return false
    }

    /// Closing the window keeps Oriel running in the menu bar.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    func showWindow() {
        if window == nil {
            let host = NSHostingController(rootView: MainWindow().environment(model))
            host.sceneBridgingOptions = [.title, .toolbars]
            let w = NSWindow(contentViewController: host)
            w.styleMask = [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView]
            w.title = "Oriel"
            w.setContentSize(NSSize(width: 980, height: 660))
            w.minSize = NSSize(width: 760, height: 520)
            w.isReleasedWhenClosed = false
            w.delegate = self
            w.setFrameAutosaveName("OrielMain")
            if !w.setFrameUsingName("OrielMain") { w.center() }
            window = w
        }
        model.reload()
        NSApp.setActivationPolicy(.regular)
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate()
    }

    /// The window is dropped when closed (not just hidden), so its views and photos are freed.
    func windowWillClose(_ notification: Notification) {
        window = nil
        NSApp.setActivationPolicy(.accessory)
    }
}
