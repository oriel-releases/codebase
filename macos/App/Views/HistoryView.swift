import OrielKit
import SwiftUI

/// History (everything) and Favorites (starred only): one page with a mode, as a grid or a list.
struct HistoryView: View {
    var favorites: Bool
    var focus: FocusState<WindowFocus?>.Binding

    @Environment(AppModel.self) private var app
    @State private var keyed: Int64?
    /// Shared by both modes for the app run.
    @AppStorage("historyAsList") private var asList = false
    @State private var setWords: String?
    @State private var banner: Banner?
    @State private var confirmDelete: HistoryEntry?

    private var shown: [HistoryEntry] { favorites ? app.entries.filter(\.isFavorite) : app.entries }

    var body: some View {
        ScrollViewReader { proxy in
        ScrollView {
            VStack(alignment: .leading, spacing: 0) {
                HStack(spacing: 10) {
                    Text(favorites ? "Favorites" : "History").pageTitle()
                    Spacer()
                    if let setWords {
                        ProgressView().controlSize(.small)
                        Text(setWords).foregroundStyle(Color.muted)
                    }
                    SegmentedPill(asList: $asList)
                }
                .padding(.top, 24)
                .padding(.bottom, 16)
                if let banner { InfoBar(banner: banner) { self.banner = nil }.padding(.bottom, 12) }
                if shown.isEmpty {
                    VStack(spacing: 12) {
                        Logo(size: 48).opacity(0.25)
                        Text(favorites ? "No favorites yet. Star a photo to keep it here." : "Nothing here yet.").foregroundStyle(Color.muted)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.top, 80)
                } else if asList {
                    LazyVStack(spacing: 8) {
                        ForEach(shown) { entry in
                            ListRow(entry: entry, actions: actions(entry)).id(entry.id)
                                .keyboardRing(keyed == entry.id && focus.wrappedValue == .content, radius: Radius.medium)
                        }
                    }
                    .keyboardGrid(shown.map(\.id), minItemWidth: .infinity, spacing: 8, selected: $keyed, focus: focus,
                                  proxy: proxy, actions: keyActions)
                } else {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 300), spacing: 12)], spacing: 12) {
                        ForEach(shown) { entry in
                            GridCard(entry: entry, actions: actions(entry), keyed: keyed == entry.id && focus.wrappedValue == .content)
                                .id(entry.id)
                        }
                    }
                    .keyboardGrid(shown.map(\.id), minItemWidth: 300, spacing: 12, selected: $keyed, focus: focus,
                                  proxy: proxy, actions: keyActions)
                }
            }
            .pageColumn()
        }
        }
        .alert(confirmDelete?.isFavorite == true ? "Delete this wallpaper?" : "Delete this wallpaper?",
               isPresented: Binding(get: { confirmDelete != nil }, set: { if !$0 { confirmDelete = nil } }), presenting: confirmDelete) { entry in
            Button("Delete", role: .destructive) { app.delete(entry) }
            Button("Cancel", role: .cancel) {}
        } message: { entry in
            Text(entry.isFavorite
                 ? "This is a favorite. The file will be deleted, and Oriel won't pick this photo again."
                 : "The file will be deleted, and Oriel won't pick this photo again.")
        }
        .onAppear { app.reload() }
    }

    /// Return sets the highlighted photo again, F stars it, Delete deletes it (after asking).
    private var keyActions: [KeyEquivalent: (Int64) -> Void] {
        func entry(_ id: Int64) -> HistoryEntry? { shown.first { $0.id == id } }
        let remove: (Int64) -> Void = { id in confirmDelete = entry(id) }
        return [
            .return: { id in if let e = entry(id) { actions(e).setAgain() } },
            "f": { id in if let e = entry(id) { app.toggleFavorite(e) } },
            .delete: remove,
            .deleteForward: remove,
        ]
    }

    private func actions(_ entry: HistoryEntry) -> EntryActions {
        EntryActions(
            canSet: !entry.filePath.isEmpty && FileManager.default.fileExists(atPath: entry.filePath) || entry.provider == "unsplash",
            busy: setWords != nil,
            setAgain: { Task { await setAgain(entry) } },
            favorite: { app.toggleFavorite(entry) },
            finder: { app.showInFinder(entry) },
            delete: { confirmDelete = entry })
    }

    private func setAgain(_ entry: HistoryEntry) async {
        guard setWords == nil else { return }
        setWords = ApplyPhase.changing.text
        banner = nil
        let sp = app.sp
        let result = await app.watchPhases({
            do {
                try await sp.wallpapers.reapply(entry)
                return ApplyResult(.applied)
            } catch {
                return ApplyResult(.failed, error: error.localizedDescription)
            }
        }, report: { words in setWords = words })
        setWords = nil
        if result.status == .failed { banner = Banner(severity: .info, title: "", message: "Couldn't re-apply: \(result.error ?? "")") }
        app.reload()
    }
}

struct EntryActions {
    var canSet: Bool
    var busy: Bool
    var setAgain: () -> Void
    var favorite: () -> Void
    var finder: () -> Void
    var delete: () -> Void
}

/// "Tue, 14 Oct 2026 · nature"
private func meta(_ entry: HistoryEntry) -> String {
    let f = DateFormatter()
    f.locale = Locale(identifier: "en_US_POSIX")
    f.dateFormat = "EEE, d MMM yyyy"
    return [f.string(from: entry.appliedAt), entry.topic].compactMap { $0 }.joined(separator: " · ")
}

private struct GridCard: View {
    var entry: HistoryEntry
    var actions: EntryActions
    var keyed = false
    @State private var hover = false

    var body: some View {
        ZStack(alignment: .bottom) {
            Photo(file: entry.filePath, url: entry.thumbnailURL, maxPixels: 640)
            Scrim().frame(height: 100).allowsHitTesting(false)
            HStack(alignment: .bottom, spacing: 8) {
                VStack(alignment: .leading, spacing: 2) {
                    Button(entry.photographerName) { open(entry.photographerURL) }
                        .buttonStyle(TextLinkStyle(color: .white))
                        .font(.system(size: 13, weight: .semibold))
                        .lineLimit(1)
                        .help("Photographer on Unsplash")
                    Text(meta(entry)).font(.system(size: 12)).foregroundStyle(.white.opacity(0.8)).lineLimit(1)
                }
                Spacer(minLength: 0)
                if hover || keyed {
                    HStack(spacing: 4) { ActionButtons(entry: entry, actions: actions, onPhoto: true) }
                }
            }
            .padding(EdgeInsets(top: 14, leading: 14, bottom: 10, trailing: 10))
        }
        .overlay(alignment: .topTrailing) {
            if entry.isFavorite {
                Image(systemName: "star.fill").font(.system(size: 16)).foregroundStyle(Color.star).padding(12)
            }
        }
        .frame(height: 210)
        .clipShape(.rounded(Radius.card))
        .contentShape(.rounded(Radius.card))
        .onHover { hover = $0 }
        .keyboardRing(keyed, radius: Radius.card)
    }
}

private struct ListRow: View {
    var entry: HistoryEntry
    var actions: EntryActions

    var body: some View {
        HStack(spacing: 14) {
            Photo(file: entry.filePath, url: entry.thumbnailURL, maxPixels: 240)
                .frame(width: 112, height: 64)
                .clipShape(.rounded(Radius.thumb))
            VStack(alignment: .leading, spacing: 2) {
                Button(entry.photographerName) { open(entry.photographerURL) }
                    .buttonStyle(TextLinkStyle())
                    .font(.system(size: 13, weight: .semibold))
                    .help("Photographer on Unsplash")
                Text(meta(entry)).font(.system(size: 12)).foregroundStyle(Color.muted)
            }
            Spacer()
            HStack(spacing: 2) { ActionButtons(entry: entry, actions: actions, onPhoto: false) }
        }
        .padding(8)
        .background(Color.surface, in: .rounded(Radius.medium))
    }
}

private struct ActionButtons: View {
    var entry: HistoryEntry
    var actions: EntryActions
    var onPhoto: Bool

    var body: some View {
        button("arrow.uturn.backward", "Set as wallpaper again", actions.setAgain).disabled(!actions.canSet || actions.busy)
        button(entry.isFavorite ? "star.fill" : "star", "Favorite: never auto-delete", actions.favorite,
               tint: entry.isFavorite ? .star : nil)
        button("folder", "Show in Finder", actions.finder)
        button("trash", "Delete", actions.delete)
    }

    @ViewBuilder
    private func button(_ icon: String, _ help: String, _ action: @escaping () -> Void, tint: Color? = nil) -> some View {
        if onPhoto {
            Button(action: action) { Image(systemName: icon).foregroundStyle(tint ?? .white) }
                .buttonStyle(OnPhotoIconStyle()).help(help).accessibilityLabel(help)
        } else {
            Button(action: action) { Image(systemName: icon).foregroundStyle(tint ?? Color.ink) }
                .buttonStyle(IconButtonStyle(size: 32)).help(help).accessibilityLabel(help)
        }
    }
}

/// Grid / list: the `ink` highlight sits under the chosen one.
private struct SegmentedPill: View {
    @Binding var asList: Bool

    var body: some View {
        HStack(spacing: 0) {
            item("square.grid.2x2", "Grid", selected: !asList) { asList = false }
            item("list.bullet", "List", selected: asList) { asList = true }
        }
        .padding(3)
        .background(Color.surface, in: .rounded(Radius.pill))
    }

    private func item(_ icon: String, _ help: String, selected: Bool, _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: 13))
                .frame(width: 34, height: 26)
                .foregroundStyle(selected ? Color.paper : Color.muted)
                .background(selected ? Color.ink : .clear, in: .rounded(Radius.tabItem))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help)
        .accessibilityLabel(help)
    }
}

private func open(_ link: String) {
    if let url = URL(string: link) { NSWorkspace.shared.open(url) }
}
