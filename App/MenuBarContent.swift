import SwiftUI

/// The menu bar menu. Next wallpaper and Pause arrive with the wallpaper engine (M2).
struct MenuBarContent: View {
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Button("Open Oriel") {
            openWindow(id: MainWindow.id)
            NSApp.activate()
        }
        Divider()
        Button("Quit Oriel") { NSApp.terminate(nil) }
            .keyboardShortcut("q")
    }
}
