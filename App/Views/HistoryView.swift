import SwiftUI
import OrielKit

struct HistoryView: View {
    @Environment(AppModel.self) private var model
    @State private var favoritesOnly = false

    private var shown: [HistoryEntry] { favoritesOnly ? model.favorites : model.entries }

    var body: some View {
        Group {
            if shown.isEmpty {
                ContentUnavailableView(favoritesOnly ? "No favorites yet" : "No photos yet",
                                       systemImage: favoritesOnly ? "star" : "photo.stack",
                                       description: Text(favoritesOnly ? "Star a photo to keep it." : "Every photo Oriel sets shows up here."))
            } else {
                ScrollView {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 16)], spacing: 16) {
                        ForEach(shown) { HistoryCard(entry: $0) }
                    }
                    .padding(20)
                }
            }
        }
        .toolbar {
            Picker("Show", selection: $favoritesOnly) {
                Text("All").tag(false)
                Text("Favorites").tag(true)
            }
            .pickerStyle(.segmented)
        }
    }
}

struct HistoryCard: View {
    @Environment(AppModel.self) private var model
    var entry: HistoryEntry
    @State private var hovering = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ZStack(alignment: .topTrailing) {
                LocalImage(path: entry.filePath, fallbackURL: entry.thumbnailURL, maxPixels: 600)
                    .aspectRatio(16 / 10, contentMode: .fit)
                    .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .overlay {
                        if hovering {
                            Button("Set as Wallpaper") { model.reapply(entry) }
                                .buttonStyle(.borderedProminent)
                                .transition(.opacity)
                        }
                    }

                Button {
                    model.toggleFavorite(entry)
                } label: {
                    Image(systemName: entry.isFavorite ? "star.fill" : "star")
                        .foregroundStyle(entry.isFavorite ? .yellow : .white)
                        .shadow(radius: 2)
                        .padding(8)
                }
                .buttonStyle(.plain)
                .opacity(entry.isFavorite || hovering ? 1 : 0)
            }

            Text(entry.photographerName)
                .font(.callout.weight(.medium))
                .lineLimit(1)
            Text("\(entry.topic.map { "\($0.capitalized) · " } ?? "")\(entry.appliedAt.formatted(date: .abbreviated, time: .shortened))")
                .font(.caption)
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
        .onHover { over in withAnimation(.easeOut(duration: 0.15)) { hovering = over } }
        .contextMenu { EntryMenu(entry: entry) }
    }
}
