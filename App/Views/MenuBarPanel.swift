import SwiftUI
import OrielKit

/// The panel under the menu bar icon: today's photo, Change now, Pause, Open, Quit.
struct MenuBarPanel: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let entry = model.current {
                LocalImage(path: entry.filePath, fallbackURL: entry.thumbnailURL, maxPixels: 700)
                    .aspectRatio(16 / 10, contentMode: .fit)
                    .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                Credit(entry: entry)
                    .font(.caption)
            } else {
                Text("No wallpaper yet").foregroundStyle(.secondary)
            }

            if let notice = model.notice {
                Text(notice).font(.caption).foregroundStyle(.orange)
            }

            HStack {
                ChangeNowButton()
                Spacer()
                Toggle("Pause", isOn: Binding(get: { model.settings.paused }, set: { model.setPaused($0) }))
                    .toggleStyle(.button)
            }

            Divider()

            HStack {
                Button("Open Oriel") { model.showWindow() }
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }
                    .keyboardShortcut("q")
            }
            .buttonStyle(.borderless)
        }
        .padding(14)
        .frame(width: 320)
        .onAppear { model.reload() }
    }
}
