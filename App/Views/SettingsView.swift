import SwiftUI
import OrielKit

struct SettingsView: View {
    @Environment(AppModel.self) private var model

    /// "Once a day" or a slideshow interval, in minutes.
    private static let intervals: [(String, Int)] = [
        ("Once a day", 0), ("Every 15 minutes", 15), ("Every 30 minutes", 30), ("Every hour", 60),
        ("Every 3 hours", 180), ("Every 6 hours", 360), ("Every 12 hours", 720),
    ]

    var body: some View {
        @Bindable var model = model
        Form {
            Section("Schedule") {
                Picker("New photo", selection: $model.settings.intervalMinutes) {
                    ForEach(Self.intervals, id: \.1) { Text($0.0).tag($0.1) }
                }
                DatePicker(model.settings.intervalMinutes == 0 ? "At" : "Starting at", selection: dailyTime,
                           displayedComponents: .hourAndMinute)
                Toggle("Catch up when I log in if I missed one", isOn: $model.settings.applyAtLogin)
                Toggle("Pause", isOn: $model.settings.paused)
            }

            Section("Display") {
                Picker("Fit", selection: $model.settings.fit) {
                    Text("Fill screen").tag(WallpaperFit.fill)
                    Text("Fit to screen").tag(WallpaperFit.fit)
                    Text("Stretch").tag(WallpaperFit.stretch)
                    Text("Center").tag(WallpaperFit.center)
                    Text("Span across screens").tag(WallpaperFit.span)
                }
                Toggle("A different photo on each screen", isOn: $model.settings.photoPerMonitor)
                    .disabled(model.settings.fit == .span)
                Toggle("Match the time of day", isOn: $model.settings.matchTimeOfDay)
                Text("Darker photos in the evening and in dark mode, brighter ones in the morning.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("Photos") {
                Toggle("Only use my favorites", isOn: $model.settings.favoritesOnly)
                Toggle("Skip less popular photos", isOn: $model.settings.skipLowLikes)
                if model.settings.skipLowLikes {
                    Stepper("At least \(model.settings.minLikes) likes", value: $model.settings.minLikes, in: 10...1000, step: 10)
                }
            }

            Section("Storage") {
                LabeledContent("Folder") {
                    Text(model.wallpaperFolder.path(percentEncoded: false).replacingOccurrences(of: NSHomeDirectory(), with: "~"))
                        .lineLimit(1).truncationMode(.middle)
                }
                HStack {
                    Button("Change…") { model.chooseWallpaperFolder() }
                    Button("Show in Finder") { NSWorkspace.shared.open(model.wallpaperFolder) }
                }
                Text("Photos you haven't starred are removed after 30 days (7 days for a slideshow).")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("General") {
                Toggle("Open Oriel when I log in", isOn: $model.settings.openAtLogin)
            }

            Section {
                LabeledContent("Version", value: model.appVersion)
                Link("Photos by Unsplash photographers", destination: URL(string: "https://unsplash.com/?utm_source=oriel&utm_medium=referral")!)
                Link("Help and ideas", destination: URL(string: "https://t.me/oriel_support_bot")!)
            }
        }
        .formStyle(.grouped)
    }

    private var dailyTime: Binding<Date> {
        Binding {
            let (h, m) = model.settings.dailyHourMinute
            return Calendar.current.date(bySettingHour: h, minute: m, second: 0, of: Date()) ?? Date()
        } set: { date in
            let c = Calendar.current.dateComponents([.hour, .minute], from: date)
            model.settings.dailyTime = String(format: "%02d:%02d", c.hour ?? 9, c.minute ?? 0)
            model.settings.timeZone = TimeZone.current.identifier
        }
    }
}
