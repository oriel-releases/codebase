import SwiftUI
import OrielKit

/// Lives in the menu bar. The main window comes and goes; the Dock icon shows only while it's open.
@main
struct OrielApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        Window("Oriel", id: MainWindow.id) {
            MainWindow()
        }
        .defaultSize(width: 960, height: 640)
        .windowResizability(.contentMinSize)

        MenuBarExtra("Oriel", systemImage: "photo.on.rectangle.angled") {
            MenuBarContent()
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        try? AppPaths().ensureCreated()
    }

    /// Closing the window keeps Oriel running in the menu bar.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}
