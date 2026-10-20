import OrielKit
import SwiftUI

struct SettingsView: View {
    @Environment(AppModel.self) private var app
    @State private var confirmLoginOff = false

    private static let intervals = [("Once a day", 0), ("Every hour", 60), ("Every 3 hours", 180), ("Every 6 hours", 360), ("Every 12 hours", 720)]

    var body: some View {
        @Bindable var app = app
        ScrollView {
            VStack(alignment: .leading, spacing: 6) {
                Text("Settings").pageTitle().padding(.top, 24).padding(.bottom, 4)

                header("Account", first: true)
                AccountCard()

                header("Schedule")
                SettingRow(icon: "photo.on.rectangle", title: "Change wallpaper",
                           description: "Once a day, or as a slideshow that keeps changing through the day.") {
                    Picker("", selection: Binding(
                        get: { Self.intervals.contains { $0.1 == app.settings.intervalMinutes } ? app.settings.intervalMinutes : 0 },
                        set: { app.settings.intervalMinutes = $0 })) {
                        ForEach(Self.intervals, id: \.1) { Text($0.0).tag($0.1) }
                    }
                    .labelsHidden().frame(minWidth: 150)
                }
                SettingRow(icon: "photo.stack", title: "Pick from",
                           description: "Favorites cycles through the photos you starred in History. With none, new photos are used.") {
                    Picker("", selection: $app.settings.favoritesOnly) {
                        Text("New photos").tag(false)
                        Text("My favorites").tag(true)
                    }
                    .labelsHidden().frame(minWidth: 150)
                }
                if app.settings.intervalMinutes == 0 {
                    SettingRow(icon: "clock", title: "Daily change time", description: "A new wallpaper once a day at this time.") {
                        TimeField(time: $app.settings.dailyTime)
                    }
                    SettingRow(icon: "person.crop.circle", title: "Change at login",
                               description: "If today's wallpaper isn't set yet when you log in, change it right away.") {
                        Toggle("", isOn: $app.settings.applyAtLogin).toggleStyle(.switch).labelsHidden()
                    }
                }
                SettingRow(icon: "keyboard", title: "Next wallpaper shortcut", description: app.hotkeyError ?? "") {
                    ShortcutRecorder(shortcut: $app.settings.hotkey)
                    Button("Clear") { app.settings.hotkey = "" }.buttonStyle(PillButtonStyle())
                }

                header("Display")
                SettingRow(icon: "rectangle.expand.vertical", title: "Fit",
                           description: "How the photo fits your screen. Span stretches one photo across all displays.") {
                    Picker("", selection: $app.settings.fit) {
                        ForEach(WallpaperFit.allCases, id: \.self) { Text($0.rawValue).tag($0) }
                    }
                    .labelsHidden().frame(minWidth: 110)
                }
                SettingRow(icon: "moon.stars", title: "Match the time of day",
                           description: "Darker photos in the evening and in dark mode, brighter ones in the morning.") {
                    Toggle("", isOn: $app.settings.matchTimeOfDay).toggleStyle(.switch).labelsHidden()
                }
                SettingRow(icon: "paintpalette", title: "Match accent color",
                           description: "Change the Mac's accent color (buttons, menus, selection) to the closest one to each new wallpaper.") {
                    Toggle("", isOn: Binding(get: { app.settings.matchAccentColor }, set: { on in
                        app.settings.matchAccentColor = on
                        // Turning it on takes effect now, not only at the next wallpaper change.
                        if on {
                            let wallpapers = app.sp.wallpapers
                            Task.detached(priority: .userInitiated) { wallpapers.applyAccentToCurrent() }
                        }
                    }))
                    .toggleStyle(.switch).labelsHidden()
                }
                if app.sp.setter.screens().count > 1 {
                    SettingRow(icon: "display.2", title: "A photo for each display",
                               description: "Give each display its own photo instead of the same one everywhere.") {
                        Toggle("", isOn: $app.settings.photoPerMonitor).toggleStyle(.switch).labelsHidden()
                    }
                }

                header("Quality")
                SettingRow(icon: "hand.thumbsup", title: "Skip photos with few likes", description: "Prefer photos other people liked on Unsplash.") {
                    Toggle("", isOn: $app.settings.skipLowLikes).toggleStyle(.switch).labelsHidden()
                }
                if app.settings.skipLowLikes {
                    SettingRow(icon: "slider.horizontal.3", title: "Minimum likes") {
                        Slider(value: Binding(get: { Double(app.settings.minLikes) },
                                              set: { app.settings.minLikes = Int(($0 / 50).rounded()) * 50 }),
                               in: 0...1000, step: 50)
                            .frame(width: 200)
                        Text("\(app.settings.minLikes)").monospacedDigit().frame(minWidth: 36, alignment: .trailing)
                    }
                }

                header("Storage")
                if let banner = app.folderBanner { InfoBar(banner: banner) { app.folderBanner = nil } }
                SettingRow(icon: "folder", title: "Wallpaper folder", description: app.wallpaperFolder.path(percentEncoded: false)) {
                    Button("Open") { app.openWallpaperFolder() }.buttonStyle(PillButtonStyle())
                    Button {
                        app.chooseWallpaperFolder()
                    } label: {
                        HStack(spacing: 6) {
                            if app.movingFolder { ProgressView().controlSize(.mini) }
                            Text("Change")
                        }
                    }
                    .buttonStyle(PillButtonStyle())
                    .disabled(app.movingFolder)
                    .help("Pick an empty folder; your photos move there.")
                }

                header("App")
                SettingRow(icon: "power", title: "Open at login", description: "Start in the menu bar when you log in.") {
                    Toggle("", isOn: Binding(get: { app.settings.startAtLogin }, set: { on in
                        if on { app.settings.startAtLogin = true } else { confirmLoginOff = true }
                    }))
                    .toggleStyle(.switch).labelsHidden()
                }
                SettingRow(icon: "arrow.triangle.2.circlepath", title: "Updates", description: app.updates.description) {
                    Button("Check for updates") { app.updates.check() }.buttonStyle(PillButtonStyle()).disabled(!app.updates.canCheck)
                }

                header("Help")
                SettingRow(icon: "bubble.left", title: "Contact support",
                           description: "Questions, bugs or ideas? Message the Oriel support bot on Telegram.") {
                    Button("Open Telegram") { app.open(OrielLinks.support) }.buttonStyle(PillButtonStyle())
                }
            }
            .tint(Color.ink)
            .pageColumn()
        }
        .alert("Turn off Open at login?", isPresented: $confirmLoginOff) {
            Button("Turn off") { app.settings.startAtLogin = false }
            Button("Keep on", role: .cancel) {}
        } message: {
            Text("Oriel won't open when you log in, so the menu bar icon and your shortcut won't work until you open it yourself. Scheduled wallpaper changes will still run.")
        }
    }

    private func header(_ title: String, first: Bool = false) -> some View {
        Text(title).sectionHeader().padding(.top, first ? 4 : 28).padding(.bottom, 8).padding(.leading, 2)
    }
}

private struct AccountCard: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        HStack(spacing: 14) {
            ZStack {
                Circle().fill(Color.paper)
                Logo(size: 22)
                if let photo = app.account?.photoUrl, photo.hasPrefix("https://") {
                    Photo(url: photo, maxPixels: 80, placeholder: .clear).clipShape(Circle())
                }
            }
            .frame(width: 40, height: 40)
            VStack(alignment: .leading, spacing: 2) {
                Text(app.account?.displayName.isEmpty == false ? app.account!.displayName : "Signed in").font(.system(size: 14))
                Text(app.account?.email ?? "").font(.system(size: 12)).foregroundStyle(Color.muted)
            }
            Spacer()
            Button("Manage account") { app.open(OrielLinks.account) }.buttonStyle(PillButtonStyle())
            Button("Sign out") { Task { await app.sp.auth.signOut() } }.buttonStyle(PillButtonStyle())
        }
        .padding(EdgeInsets(top: 12, leading: 20, bottom: 12, trailing: 16))
        .frame(minHeight: 64)
        .background(Color.surface, in: .rounded(Radius.card))
    }
}

/// A 24-hour time in 5-minute steps ("HH:mm").
struct TimeField: View {
    @Binding var time: String

    var body: some View {
        let parts = time.split(separator: ":").compactMap { Int($0) }
        let hour = parts.first ?? 9
        let minute = parts.count > 1 ? parts[1] / 5 * 5 : 0
        HStack(spacing: 4) {
            Picker("", selection: Binding(get: { hour }, set: { time = String(format: "%02d:%02d", $0, minute) })) {
                ForEach(0..<24, id: \.self) { Text(String(format: "%02d", $0)).tag($0) }
            }
            .labelsHidden().frame(width: 64)
            Text(":")
            Picker("", selection: Binding(get: { minute }, set: { time = String(format: "%02d:%02d", hour, $0) })) {
                ForEach(Array(stride(from: 0, to: 60, by: 5)), id: \.self) { Text(String(format: "%02d", $0)).tag($0) }
            }
            .labelsHidden().frame(width: 64)
        }
    }
}

/// Click, then press keys: the next valid combination is saved (and registered at once).
struct ShortcutRecorder: View {
    @Binding var shortcut: String
    @State private var recording = false
    @State private var monitor: Any?

    var body: some View {
        Button {
            recording ? stop() : start()
        } label: {
            Text(recording ? "Click, then press keys" : (Hotkey.parse(shortcut)?.display ?? "Click, then press keys"))
                .foregroundStyle(recording || shortcut.isEmpty ? Color.muted : Color.ink)
                .frame(width: 150)
        }
        .buttonStyle(PillButtonStyle())
        .onDisappear { stop() }
    }

    private func start() {
        recording = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if event.keyCode == 53 { // Escape: stop without a change
                stop()
                return nil
            }
            if let key = Hotkey(event: event) {
                shortcut = key.storage
                stop()
            }
            return nil
        }
    }

    private func stop() {
        recording = false
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
    }
}
