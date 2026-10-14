import SwiftUI
import OrielKit

struct HomeView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                if !model.hasKey {
                    Banner(icon: "key.slash", text: "This build has no Unsplash key, so Oriel can't fetch photos. Add it to .env and run scripts/configure.sh.")
                }
                if let notice = model.notice {
                    Banner(icon: "exclamationmark.triangle", text: notice)
                } else if let error = model.state.lastError {
                    Banner(icon: "wifi.exclamationmark", text: retryText(error))
                }

                hero

                HStack(spacing: 12) {
                    ChangeNowButton()
                        .controlSize(.large)
                    Toggle("Pause", isOn: Binding(get: { model.settings.paused }, set: { model.setPaused($0) }))
                        .toggleStyle(.button)
                        .controlSize(.large)
                    Spacer()
                    Text(scheduleText)
                        .foregroundStyle(.secondary)
                }
            }
            .padding(24)
            .frame(maxWidth: 1100)
            .frame(maxWidth: .infinity)
        }
    }

    @ViewBuilder
    private var hero: some View {
        if let entry = model.current {
            ZStack(alignment: .bottomLeading) {
                LocalImage(path: entry.filePath, fallbackURL: entry.thumbnailURL, maxPixels: 2000)
                    .aspectRatio(16 / 10, contentMode: .fit)
                    .clipped()

                LinearGradient(colors: [.clear, .black.opacity(0.55)], startPoint: .center, endPoint: .bottom)

                HStack(alignment: .bottom) {
                    VStack(alignment: .leading, spacing: 4) {
                        if let description = entry.description {
                            Text(description.prefix(1).uppercased() + description.dropFirst())
                                .font(.title3.weight(.semibold))
                                .lineLimit(2)
                        }
                        Credit(entry: entry)
                    }
                    Spacer()
                    Button {
                        model.toggleFavorite(entry)
                    } label: {
                        Image(systemName: entry.isFavorite ? "star.fill" : "star")
                            .foregroundStyle(entry.isFavorite ? .yellow : .white)
                            .font(.title2)
                    }
                    .buttonStyle(.plain)
                    .help(entry.isFavorite ? "Remove from favorites" : "Add to favorites")
                }
                .foregroundStyle(.white)
                .padding(20)
            }
            .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
            .shadow(color: .black.opacity(0.2), radius: 12, y: 6)
            .contextMenu { EntryMenu(entry: entry) }
        } else {
            ContentUnavailableView {
                Label("No wallpaper yet", systemImage: "photo.on.rectangle.angled")
            } description: {
                Text(model.isChanging ? "Finding your first photo…" : "Click Change now for your first photo.")
            }
            .frame(height: 360)
        }
    }

    private var scheduleText: String {
        let s = model.settings
        if s.paused { return "Paused" }
        let next = ScheduleRules.nextTick(s, Date())
        let when = next.formatted(Calendar.current.isDateInToday(next) ? .dateTime.hour().minute() : .dateTime.weekday().hour().minute())
        return "Next photo \(when)"
    }

    private func retryText(_ error: String) -> String {
        guard let retry = model.state.nextRetryAt, retry > Date() else { return error }
        return "\(error) Trying again at \(retry.formatted(date: .omitted, time: .shortened))."
    }
}

/// "Photo by Jane Doe on Unsplash", both linked, as Unsplash's guidelines ask.
struct Credit: View {
    var entry: HistoryEntry

    var body: some View {
        HStack(spacing: 4) {
            Text("Photo by")
            Link(entry.photographerName, destination: URL(string: entry.photographerURL) ?? URL(string: "https://unsplash.com")!)
                .fontWeight(.semibold)
            Text("on")
            Link("Unsplash", destination: URL(string: entry.photoPageURL) ?? URL(string: "https://unsplash.com")!)
                .fontWeight(.semibold)
        }
        .font(.callout)
        .tint(.primary)
    }
}

struct ChangeNowButton: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        Button {
            model.changeNow()
        } label: {
            if model.isChanging {
                HStack(spacing: 6) {
                    ProgressView().controlSize(.small)
                    Text("Changing…")
                }
            } else {
                Label("Change now", systemImage: "arrow.trianglehead.2.clockwise")
            }
        }
        .buttonStyle(.borderedProminent)
        .disabled(model.isChanging || !model.hasKey)
        .help(model.changeNowAvailableAt.map { "Available again at \($0.formatted(date: .omitted, time: .shortened))" }
              ?? "A new photo right now")
    }
}

/// Set as wallpaper, favorite, show in Finder, open on Unsplash, delete.
struct EntryMenu: View {
    @Environment(AppModel.self) private var model
    var entry: HistoryEntry

    var body: some View {
        Button("Set as Wallpaper") { model.reapply(entry) }
        Button(entry.isFavorite ? "Remove from Favorites" : "Add to Favorites") { model.toggleFavorite(entry) }
        Divider()
        Button("Show in Finder") { model.showInFinder(entry) }
        Button("Open on Unsplash") { model.open(entry.photoPageURL) }
        Divider()
        Button("Delete", role: .destructive) { model.delete(entry) }
    }
}

struct Banner: View {
    var icon: String
    var text: String

    var body: some View {
        Label(text, systemImage: icon)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(12)
            .background(.orange.opacity(0.15), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
    }
}
