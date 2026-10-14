import OrielKit
import SwiftUI

struct SearchView: View {
    var focus: FocusState<WindowFocus?>.Binding

    @Environment(AppModel.self) private var app
    @FocusState private var focused: Bool

    var body: some View {
        let search = app.search
        ScrollView {
            VStack(alignment: .leading, spacing: 0) {
                SearchBar(focused: $focused)
                if let banner = search.banner {
                    InfoBar(banner: banner) { search.banner = nil }.padding(.top, 12)
                }
                if search.isStart { StartContent().padding(.top, 22) } else { Results(focus: focus).padding(.top, 22) }
            }
            .pageColumn(top: 32)
        }
        // The field doesn't take the keyboard by itself: arrowing through the sidebar must pass Search.
        .onAppear { Task { await search.findCovers() } }
    }
}

private struct SearchBar: View {
    @Environment(AppModel.self) private var app
    var focused: FocusState<Bool>.Binding

    var body: some View {
        @Bindable var search = app.search
        HStack(spacing: 10) {
            Image(systemName: "magnifyingglass").font(.system(size: 16)).foregroundStyle(Color.muted)
            TextField("Search wallpapers, like northern lights or tokyo night", text: $search.text)
                .textFieldStyle(.plain)
                .font(.system(size: 15))
                .focused(focused)
                .onSubmit { Task { await search.submit() } }
            if !search.text.isEmpty {
                Button {
                    search.clear()
                } label: {
                    Image(systemName: "xmark")
                }
                .buttonStyle(IconButtonStyle(size: 30))
                .help("Clear")
            }
            Button {
                Task { await search.submit() }
            } label: {
                HStack(spacing: 6) {
                    if search.isSearching { ProgressView().controlSize(.small).tint(Color.paper) }
                    Text("Search")
                }
                .padding(.horizontal, 6)
            }
            .buttonStyle(PrimaryButtonStyle(height: 40))
            .clipShape(.rounded(20))
            .disabled(search.isSearching || search.text.trimmingCharacters(in: .whitespaces).isEmpty)
        }
        .padding(.leading, 18)
        .padding(.trailing, 6)
        .frame(height: 52)
        .background(Color.surface, in: .rounded(Radius.bar))
    }
}

private struct StartContent: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        let search = app.search
        VStack(alignment: .leading, spacing: 22) {
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    Text("Need an idea?").font(.system(size: 14, weight: .semibold))
                    Spacer()
                    Button {
                        search.shuffle()
                    } label: {
                        Label("Shuffle", systemImage: "shuffle")
                    }
                    .buttonStyle(PillButtonStyle())
                }
                FlowLayout(spacing: 8) {
                    ForEach(search.ideas, id: \.self) { idea in
                        Button {
                            Task { await search.search(idea) }
                        } label: {
                            HStack(spacing: 6) {
                                Image(systemName: "magnifyingglass").font(.system(size: 12)).foregroundStyle(Color.muted)
                                Text(idea)
                            }
                            .font(.system(size: 13))
                            .padding(.horizontal, 12)
                            .padding(.vertical, 7)
                            .background(Color.paper, in: .rounded(Radius.pill))
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .padding(20)
            .background(Color.surface, in: .rounded(Radius.large))

            Button {
                withAnimation(.easeOut(duration: 0.25)) { app.showingInterests = true }
            } label: {
                HStack(spacing: 16) {
                    Image(systemName: "heart").font(.system(size: 18)).foregroundStyle(Color.muted)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Interests").font(.system(size: 14, weight: .semibold))
                        Text("\(app.settings.topics.filter(\.enabled).count) of \(app.settings.topics.count) on. Each new wallpaper comes from one of them.")
                            .font(.system(size: 12)).foregroundStyle(Color.muted)
                    }
                    Spacer()
                    Image(systemName: "chevron.right").font(.system(size: 12)).foregroundStyle(Color.muted)
                }
                .padding(EdgeInsets(top: 14, leading: 20, bottom: 14, trailing: 16))
                .frame(minHeight: 68)
                .background(Color.surface, in: .rounded(Radius.card))
                .contentShape(.rounded(Radius.card))
            }
            .buttonStyle(.plain)
            .foregroundStyle(Color.ink)
        }
        .onAppear { search.shuffle() }
    }
}

private struct Results: View {
    var focus: FocusState<WindowFocus?>.Binding

    @Environment(AppModel.self) private var app
    @State private var keyed: String?

    var body: some View {
        let search = app.search
        ScrollViewReader { proxy in
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text("Results for “\(search.query ?? "")”").sectionHeader()
                Spacer()
                if search.canAddToInterests {
                    Button {
                        search.addToInterests()
                    } label: {
                        Label("Add to Interests", systemImage: "plus")
                    }
                    .buttonStyle(PillButtonStyle())
                    .help("Add this search to your Interests, so the daily wallpaper can come from it")
                }
            }
            if search.results.isEmpty && !search.isSearching {
                VStack(spacing: 10) {
                    Image(systemName: "magnifyingglass").font(.system(size: 28)).foregroundStyle(Color.muted)
                    Text("No photos found").font(.system(size: 14, weight: .semibold))
                    Text("Try other or fewer words. Only photos big enough for your screen are shown.")
                        .foregroundStyle(Color.muted).multilineTextAlignment(.center).frame(maxWidth: 360)
                }
                .padding(.vertical, 24)
                .padding(.horizontal, 40)
                .frame(maxWidth: .infinity)
                .background(Color.surface, in: .rounded(Radius.large))
            } else {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 280), spacing: 14)], spacing: 14) {
                    ForEach(search.results) { result in
                        ResultCard(result: result).id(result.id)
                            .keyboardRing(keyed == result.id && focus.wrappedValue == .content, radius: Radius.card)
                    }
                }
                // Return sets the highlighted photo.
                .keyboardGrid(search.results.map(\.id), minItemWidth: 280, spacing: 14, selected: $keyed, focus: focus, proxy: proxy,
                              actions: [.return: { id in
                                  if let r = search.results.first(where: { $0.id == id }) { Task { await search.set(r) } }
                              }])
                if search.hasMore {
                    Button {
                        Task { await search.loadMore() }
                    } label: {
                        HStack(spacing: 6) {
                            if search.isSearching { ProgressView().controlSize(.mini) }
                            Text("Show more")
                        }
                    }
                    .buttonStyle(PillButtonStyle())
                    .disabled(search.isSearching)
                    .frame(maxWidth: .infinity)
                    .padding(.top, 16)
                } else if search.isSearching {
                    ProgressView().frame(maxWidth: .infinity).padding(.top, 16)
                }
            }
        }
        }
    }
}

private struct ResultCard: View {
    @Environment(AppModel.self) private var app
    var result: SearchModel.Result

    var body: some View {
        let search = app.search
        let photo = result.photo
        ZStack(alignment: .bottom) {
            Photo(url: photo.thumbnailURL.absoluteString, maxPixels: 720)
            Scrim().frame(height: 90).allowsHitTesting(false)
            HStack {
                Button(photo.photographerName) { NSWorkspace.shared.open(photo.photographerURL) }
                    .buttonStyle(TextLinkStyle(color: .white))
                    .font(.system(size: 12))
                    .lineLimit(1)
                    .help("Photographer on Unsplash")
                Spacer()
                Button {
                    Task { await search.set(result) }
                } label: {
                    HStack(spacing: 6) {
                        if search.settingId == result.id { ProgressView().controlSize(.mini) }
                        Text(label(search))
                    }
                }
                .buttonStyle(OnPhotoPillStyle())
                .disabled(search.settingId != nil)
            }
            .padding(EdgeInsets(top: 14, leading: 10, bottom: 10, trailing: 10))
        }
        .frame(height: 210)
        .clipShape(.rounded(Radius.card))
        .help("\(photo.width) × \(photo.height)")
        .accessibilityLabel(photo.description ?? "Photo by \(photo.photographerName)")
    }

    private func label(_ search: SearchModel) -> String {
        if search.settingId == result.id { return search.settingWords ?? ApplyPhase.downloading.text }
        if search.setId == result.id { return "Wallpaper set ✓" }
        return "Set as wallpaper"
    }
}

struct InterestsView: View {
    var focus: FocusState<WindowFocus?>.Binding

    @Environment(AppModel.self) private var app
    @State private var keyed: String?

    var body: some View {
        @Bindable var app = app
        let builtIn = app.settings.topics.indices.filter { !app.settings.topics[$0].isCustom }
        let custom = app.settings.topics.indices.filter { app.settings.topics[$0].isCustom }
        let all = builtIn + custom
        ScrollViewReader { proxy in
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                HStack(spacing: 12) {
                    Button {
                        withAnimation(.easeOut(duration: 0.25)) { app.showingInterests = false }
                    } label: {
                        Image(systemName: "chevron.left").font(.system(size: 14))
                    }
                    .buttonStyle(IconButtonStyle())
                    .help("Back")
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Interests").pageTitle()
                        Text("\(app.settings.topics.filter(\.enabled).count) of \(app.settings.topics.count) on. Each new wallpaper comes from one of them.")
                            .font(.system(size: 13)).foregroundStyle(Color.muted)
                    }
                    Spacer()
                    Button("Select all") { setAll(true) }.buttonStyle(PillButtonStyle())
                    Button("Clear") { setAll(false) }.buttonStyle(PillButtonStyle())
                }
                if app.settings.enabledQueries().isEmpty {
                    InfoBar(banner: Banner(severity: .warning, title: "", message: "Nothing is on, so Oriel uses general landscape photos.", closable: false))
                }
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 190), spacing: 12)], spacing: 12) {
                    ForEach(builtIn, id: \.self) { i in
                        let ringed = keyed == app.settings.topics[i].query && focus.wrappedValue == .content
                        let topic = app.settings.topics[i]
                        InterestTile(label: topic.query.capitalizedFirst, cover: InterestCovers.all[topic.query].map { $0.url().absoluteString },
                                     credit: InterestCovers.all[topic.query].map { "Photo by \($0.by) on Unsplash" },
                                     on: topic.enabled, height: 136, radius: Radius.card) {
                            app.settings.topics[i].enabled.toggle()
                        }
                        .id(topic.query)
                        .keyboardRing(ringed, radius: Radius.card)
                    }
                }
                // Return or Space turns the highlighted interest on or off (your own ones are reached with → at the end).
                .keyboardGrid(all.map { app.settings.topics[$0].query }, minItemWidth: 190, spacing: 12, selected: $keyed,
                              focus: focus, proxy: proxy, actions: [.return: toggle, .space: toggle])
                VStack(alignment: .leading, spacing: 12) {
                    Text("Your own").sectionHeader()
                    Text("Search for anything, then press Add to Interests to make it one of yours.")
                        .font(.system(size: 13)).foregroundStyle(Color.muted)
                    if !custom.isEmpty {
                        LazyVGrid(columns: [GridItem(.adaptive(minimum: 190), spacing: 12)], spacing: 12) {
                            ForEach(custom, id: \.self) { i in
                                let topic = app.settings.topics[i]
                                InterestTile(label: topic.query.capitalizedFirst, cover: topic.coverURL, credit: nil, on: topic.enabled,
                                             height: 136, radius: Radius.card,
                                             emptyText: app.search.findingCover == topic.query ? "Finding a photo…" : "Photo comes soon",
                                             onRemove: { app.settings.topics.removeAll { $0.query == topic.query && $0.isCustom } }) {
                                    app.settings.topics[i].enabled.toggle()
                                }
                                .id(topic.query)
                                .keyboardRing(keyed == topic.query && focus.wrappedValue == .content, radius: Radius.card)
                            }
                        }
                    }
                }
                .padding(.top, 8)
            }
            .pageColumn(top: 32)
        }
        }
        .onAppear { Task { await app.search.findCovers() } }
    }

    private func toggle(_ query: String) {
        if let i = app.settings.topics.firstIndex(where: { $0.query == query }) { app.settings.topics[i].enabled.toggle() }
    }

    /// Select all and Clear affect the built-in interests only.
    private func setAll(_ on: Bool) {
        for i in app.settings.topics.indices where !app.settings.topics[i].isCustom { app.settings.topics[i].enabled = on }
    }
}

/// A photo tile for an interest: the photo zooms a little on hover; a check circle and a white border when on.
struct InterestTile: View {
    var label: String
    var cover: String?
    var credit: String?
    var on: Bool
    var height: CGFloat
    var radius: CGFloat
    var emptyText = "Photo comes soon"
    var onRemove: (() -> Void)? = nil
    var toggle: () -> Void

    @State private var hover = false

    var body: some View {
        Button(action: toggle) {
            ZStack(alignment: .bottomLeading) {
                Color.tile
                if let cover {
                    Photo(url: cover, maxPixels: 400, placeholder: .tile)
                        .scaleEffect(hover ? 1.06 : 1)
                        .animation(.easeOut(duration: 0.3), value: hover)
                        .help(credit ?? "")
                } else {
                    VStack(spacing: 6) {
                        Image("LogoDark").resizable().frame(width: 30, height: 30).opacity(0.35)
                        Text(emptyText).font(.system(size: 12)).foregroundStyle(.white.opacity(0.6))
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                }
                Scrim().allowsHitTesting(false)
                Text(label)
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(.white)
                    .lineLimit(1)
                    .padding(EdgeInsets(top: 0, leading: 14, bottom: 12, trailing: 14))
            }
            .frame(height: height)
            .overlay(alignment: .topTrailing) {
                ZStack {
                    if on {
                        Circle().fill(.white)
                        Image(systemName: "checkmark").font(.system(size: 12, weight: .bold)).foregroundStyle(Color(hex: 0x141414))
                    } else {
                        Circle().fill(.black.opacity(0.2))
                        Circle().strokeBorder(.white.opacity(0.8), lineWidth: 1.5)
                    }
                }
                .frame(width: 24, height: 24)
                .padding(10)
            }
            .clipShape(.rounded(radius))
            .overlay { if on { RoundedRectangle(cornerRadius: radius, style: .continuous).strokeBorder(.white, lineWidth: 2) } }
            .contentShape(.rounded(radius))
        }
        .buttonStyle(.plain)
        .overlay(alignment: .topLeading) {
            if let onRemove {
                Button(action: onRemove) {
                    Image(systemName: "xmark").font(.system(size: 10, weight: .bold)).foregroundStyle(.white)
                        .frame(width: 28, height: 28).background(.black.opacity(0.4), in: Circle())
                }
                .buttonStyle(.plain)
                .padding(8)
                .help("Remove")
            }
        }
        .onHover { hover = $0 }
        .accessibilityLabel(label)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// Lays children out left to right, wrapping like words.
struct FlowLayout: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let rows = arrange(width: proposal.width ?? .infinity, subviews)
        return CGSize(width: proposal.width ?? rows.map(\.width).max() ?? 0, height: rows.last.map { $0.y + $0.height } ?? 0)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        for row in arrange(width: bounds.width, subviews) {
            var x = bounds.minX
            for i in row.items {
                let size = subviews[i].sizeThatFits(.unspecified)
                subviews[i].place(at: CGPoint(x: x, y: bounds.minY + row.y), proposal: .unspecified)
                x += size.width + spacing
            }
        }
    }

    private struct Row { var items: [Int] = []; var y: CGFloat = 0; var width: CGFloat = 0; var height: CGFloat = 0 }

    private func arrange(width: CGFloat, _ subviews: Subviews) -> [Row] {
        var rows: [Row] = []
        var row = Row()
        for (i, view) in subviews.enumerated() {
            let size = view.sizeThatFits(.unspecified)
            if !row.items.isEmpty && row.width + spacing + size.width > width {
                rows.append(row)
                row = Row(y: row.y + row.height + spacing)
            }
            row.width += (row.items.isEmpty ? 0 : spacing) + size.width
            row.height = max(row.height, size.height)
            row.items.append(i)
        }
        if !row.items.isEmpty { rows.append(row) }
        return rows
    }
}
