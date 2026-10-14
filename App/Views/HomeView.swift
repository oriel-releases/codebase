import AppKit
import OrielKit
import SwiftUI

struct HomeView: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Home").pageTitle().padding(.top, 24).padding(.bottom, 4)
                if let banner = app.homeBanner {
                    InfoBar(banner: banner) { app.homeBanner = nil }
                }
                Preview()
                ActionsRow()
                if app.today != nil { TodayCard().padding(.top, 8) }
            }
            .pageColumn()
        }
        .onAppear {
            app.reload()
            app.loadToday()
        }
    }
}

/// The current wallpaper, 440 high. Double-click (or keep clicking quickly) to favorite it, with a star burst.
private struct Preview: View {
    @Environment(AppModel.self) private var app
    @State private var stars: [Star] = []
    @State private var lastClick = Date.distantPast

    struct Star: Identifiable {
        let id = UUID()
        var at: CGPoint
        var rotation: Double
    }

    var body: some View {
        ZStack {
            Color.surface
            if let entry = app.current {
                Photo(file: entry.filePath, url: entry.thumbnailURL, maxPixels: 1200, placeholder: .surface)
                    .accessibilityLabel("Current wallpaper")
            } else {
                VStack(spacing: 12) {
                    Logo(size: 48).opacity(0.25)
                    Text("No wallpaper yet. Press Change now.").foregroundStyle(Color.muted)
                }
            }
            ZStack {
                ForEach(stars) { star in
                    BurstStar(rotation: star.rotation) { stars.removeAll { $0.id == star.id } }
                        .position(star.at)
                }
            }
            .allowsHitTesting(false)
        }
        .frame(height: 440)
        .clipShape(.rounded(Radius.large))
        .contentShape(.rounded(Radius.large))
        .onTapGesture(coordinateSpace: .local) { point in
            // A click within 350 ms of the previous one is a like; every further quick click is another.
            let now = Date()
            defer { lastClick = now }
            guard now.timeIntervalSince(lastClick) <= 0.35, app.current != nil else { return }
            stars.append(Star(at: point, rotation: Double(Int.random(in: -15...15))))
            app.like()
        }
    }
}

/// One gold star: grows from 0.3 to 1.25 (170 ms) to 2.0, rises 150 pt after 110 ms, fades after 55%, 950 ms in all.
private struct BurstStar: View {
    var rotation: Double
    var done: () -> Void

    var body: some View {
        Image(systemName: "star.fill")
            .font(.system(size: 72))
            .foregroundStyle(Color.star)
            .keyframeAnimator(initialValue: Frame(rotation: rotation), trigger: true) { content, f in
                content
                    .scaleEffect(f.scale)
                    .rotationEffect(.degrees(f.rotation))
                    .offset(y: f.y)
                    .opacity(f.opacity)
            } keyframes: { _ in
                KeyframeTrack(\.scale) {
                    CubicKeyframe(0.3, duration: 0.001)
                    CubicKeyframe(1.25, duration: 0.169)
                    CubicKeyframe(2.0, duration: 0.78)
                }
                KeyframeTrack(\.rotation) { CubicKeyframe(0, duration: 0.95) }
                KeyframeTrack(\.y) {
                    LinearKeyframe(0, duration: 0.11)
                    CubicKeyframe(-150, duration: 0.84)
                }
                KeyframeTrack(\.opacity) {
                    LinearKeyframe(1, duration: 0.522)
                    CubicKeyframe(0, duration: 0.428)
                }
            }
            .task {
                try? await Task.sleep(for: .seconds(0.95))
                done()
            }
    }

    struct Frame {
        var scale = 0.3
        var rotation: Double
        var y: Double = 0
        var opacity = 1.0
    }
}

/// "Photo by X on Unsplash", then Share, Lock, Favorite and Change now.
private struct ActionsRow: View {
    @Environment(AppModel.self) private var app
    @State private var shareAnchor: NSView?
    @State private var sharing = false

    var body: some View {
        HStack(spacing: 16) {
            if let entry = app.current {
                Credit(name: entry.photographerName, photographer: entry.photographerURL, page: entry.photoPageURL)
                    .padding(.leading, 4)
            }
            Spacer(minLength: 0)
            HStack(spacing: 8) {
                Button {
                    Task { await share() }
                } label: {
                    Image(systemName: app.shareCopied ? "checkmark" : "square.and.arrow.up")
                }
                .buttonStyle(IconButtonStyle())
                .background(AnchorView(view: $shareAnchor))
                .disabled(!(app.current.map(ShareService.canShare) ?? false) || sharing)
                .help("Share this wallpaper. Whoever opens the link can set it in one click.")
                .accessibilityLabel("Share this wallpaper")

                Button {
                    app.setPaused(!app.settings.paused)
                } label: {
                    Image(systemName: app.settings.paused ? "lock.fill" : "lock.open")
                }
                .buttonStyle(IconButtonStyle())
                .help("Lock to keep this wallpaper. Unlock to let it change.")
                .accessibilityLabel("Keep this wallpaper")

                Button {
                    if let entry = app.current { app.toggleFavorite(entry) }
                } label: {
                    Image(systemName: app.current?.isFavorite == true ? "star.fill" : "star")
                        .foregroundStyle(app.current?.isFavorite == true ? Color.star : Color.ink)
                }
                .buttonStyle(IconButtonStyle())
                .disabled(app.current == nil)
                .help("Favorite: keep this one forever")
                .accessibilityLabel("Favorite")

                ChangeNowButton()
            }
        }
    }

    private func share() async {
        guard let entry = app.current else { return }
        sharing = true
        defer { sharing = false }
        do {
            let link = try await app.sp.share.link(entry)
            app.sp.stats.count(StatsReporter.sharesSent)
            let text = "A wallpaper I like. Open this link to set it on your computer in one click:"
            if !SharePicker.show([text, URL(string: link)!], title: "A wallpaper for you", from: shareAnchor) {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(link, forType: .string)
                app.shareCopied = true
                try? await Task.sleep(for: .seconds(2))
                app.shareCopied = false
            }
        } catch {
            app.homeBanner = Banner(severity: .warning, title: "Couldn't share", message: error.localizedDescription)
        }
    }
}

struct ChangeNowButton: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        Button {
            Task { await app.changeNow() }
        } label: {
            HStack(spacing: 8) {
                if app.isBusy {
                    ProgressView().controlSize(.small).tint(Color.paper)
                } else {
                    Image(systemName: app.limitMinutes == nil ? "arrow.clockwise" : "clock").font(.system(size: 14))
                }
                SlidingText(text: label)
            }
        }
        .buttonStyle(PrimaryButtonStyle(height: 36))
        .disabled(app.isBusy || app.limitMinutes != nil)
        .accessibilityLabel("Change wallpaper now")
    }

    private var label: String {
        if app.isBusy { return app.phaseWords ?? ApplyPhase.finding.text }
        if let n = app.limitMinutes { return "Try after \(n) min" }
        return "Change now"
    }
}

/// "Photo by {name} on {Unsplash}", both links.
struct Credit: View {
    var name: String
    var photographer: String
    var page: String
    var color: Color = .ink
    var mutedColor: Color = .muted

    var body: some View {
        HStack(spacing: 2) {
            Text("Photo by").foregroundStyle(mutedColor)
            Button(name) { open(photographer) }
                .buttonStyle(TextLinkStyle(color: color))
                .fontWeight(.semibold)
                .padding(.horizontal, 3)
                .help("Photographer on Unsplash")
            Text("on").foregroundStyle(mutedColor)
            Button("Unsplash") { open(page) }
                .buttonStyle(TextLinkStyle(color: color))
                .fontWeight(.semibold)
                .padding(.horizontal, 3)
        }
        .font(.system(size: 13))
        .lineLimit(1)
    }

    private func open(_ link: String) {
        if let url = URL(string: link) { NSWorkspace.shared.open(url) }
    }
}

private struct TodayCard: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        if let today = app.today {
            HStack(spacing: 16) {
                Photo(url: today.thumbnailURL(width: 480).absoluteString, maxPixels: 480)
                    .frame(width: 128, height: 80)
                    .clipShape(.rounded(Radius.medium))
                VStack(alignment: .leading, spacing: 4) {
                    Text("Wallpaper of the day").font(.system(size: 14, weight: .semibold))
                    HStack(spacing: 3) {
                        Text("Photo by").foregroundStyle(Color.muted)
                        Button(today.by) { NSWorkspace.shared.open(today.authorURL) }.buttonStyle(TextLinkStyle())
                        Text("on Unsplash").foregroundStyle(Color.muted)
                    }
                    .font(.system(size: 12))
                }
                Spacer()
                Button {
                    Task { await app.setToday() }
                } label: {
                    HStack(spacing: 6) {
                        if app.todayWords != nil { ProgressView().controlSize(.mini) }
                        Text(app.todayIsCurrent ? "On your desktop ✓" : app.todayWords ?? "Set as wallpaper")
                    }
                }
                .buttonStyle(PillButtonStyle())
                .disabled(app.todayIsCurrent || app.todayWords != nil)
            }
            .padding(EdgeInsets(top: 10, leading: 10, bottom: 10, trailing: 16))
            .background(Color.surface, in: .rounded(Radius.card))
            .accessibilityElement(children: .contain)
            .accessibilityLabel("Wallpaper of the day")
        }
    }
}

/// Gives SwiftUI code the NSView of a button, to anchor the share picker to it.
struct AnchorView: NSViewRepresentable {
    @Binding var view: NSView?

    func makeNSView(context: Context) -> NSView {
        let v = NSView()
        DispatchQueue.main.async { view = v }
        return v
    }

    func updateNSView(_ nsView: NSView, context: Context) {}
}

/// The macOS share picker (Messages, Mail, AirDrop, Notes and the apps the person has).
@MainActor
enum SharePicker {
    /// False when it can't be shown (then the caller copies the link).
    static func show(_ items: [Any], title: String, from anchor: NSView?) -> Bool {
        guard let anchor, anchor.window != nil else { return false }
        let picker = NSSharingServicePicker(items: items)
        picker.show(relativeTo: anchor.bounds, of: anchor, preferredEdge: .minY)
        return true
    }
}
