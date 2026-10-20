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

/// The current wallpaper, 440 high. A click within 350 ms of the previous one is a "like": it becomes a favorite (never
/// un-favorites) and a gold star bursts up where you clicked. Keep clicking: a star every time, as many as you like.
private struct Preview: View {
    @Environment(AppModel.self) private var app
    @State private var stars: [Star] = []
    @State private var lastPress = Date.distantPast
    @State private var pressing = false

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
        }
        .frame(height: 440)
        .frame(maxWidth: .infinity)
        // The stars may fly above the photo's top edge, so they're drawn over it, not clipped with it.
        .clipShape(.rounded(Radius.large))
        .overlay {
            ZStack {
                ForEach(stars) { star in
                    BurstStar(rotation: star.rotation) { stars.removeAll { $0.id == star.id } }
                        .position(star.at)
                }
            }
            .allowsHitTesting(false)
        }
        .contentShape(.rounded(Radius.large))
        // On press, not release (like Windows), so quick clicks feel immediate.
        .gesture(DragGesture(minimumDistance: 0, coordinateSpace: .local)
            .onChanged { drag in
                guard !pressing else { return }
                pressing = true
                pressed(at: drag.startLocation)
            }
            .onEnded { _ in pressing = false })
    }

    private func pressed(at point: CGPoint) {
        let now = Date()
        let quick = now.timeIntervalSince(lastPress) < 0.35
        lastPress = now
        guard quick, app.current != nil else { return }
        stars.append(Star(at: point, rotation: Double(Int.random(in: -15...15))))
        app.like()
    }
}

/// One gold star, exactly like Windows: 72 pt, pops from 0.3 to 1.25 (170 ms) and keeps growing to 2.0, stays put for
/// 110 ms then rises 150 pt, turns from its random tilt to straight, and fades over the last 45%, all in 950 ms. Every
/// step eases out (cubic).
private struct BurstStar: View {
    var rotation: Double
    var done: () -> Void

    @State private var play = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// Cubic ease-out, cubic-bezier(0.215, 0.61, 0.355, 1).
    private static let easeOut = UnitCurve.bezier(startControlPoint: UnitPoint(x: 0.215, y: 0.61),
                                                  endControlPoint: UnitPoint(x: 0.355, y: 1))

    struct Frame {
        var scale = 0.3
        var rotation: Double
        var y: Double = 0
        var opacity = 1.0
    }

    var body: some View {
        let still = reduceMotion
        return Image(systemName: "star.fill")
            .font(.system(size: 72))
            .foregroundStyle(Color.star)
            .shadow(color: .black.opacity(0.18), radius: 6, y: 2)
            .keyframeAnimator(initialValue: Frame(rotation: rotation), trigger: play) { content, f in
                content
                    .scaleEffect(still ? 1 : f.scale)
                    .rotationEffect(.degrees(still ? 0 : f.rotation))
                    .offset(y: still ? 0 : f.y)
                    .opacity(f.opacity)
            } keyframes: { _ in
                KeyframeTrack(\.scale) {
                    LinearKeyframe(1.25, duration: 0.17, timingCurve: Self.easeOut)
                    LinearKeyframe(2.0, duration: 0.78, timingCurve: Self.easeOut)
                }
                KeyframeTrack(\.y) {
                    LinearKeyframe(0, duration: 0.11)
                    LinearKeyframe(-150, duration: 0.84, timingCurve: Self.easeOut)
                }
                KeyframeTrack(\.rotation) {
                    LinearKeyframe(0, duration: 0.95, timingCurve: Self.easeOut)
                }
                KeyframeTrack(\.opacity) {
                    LinearKeyframe(1, duration: 0.5225)
                    LinearKeyframe(0, duration: 0.4275, timingCurve: Self.easeOut)
                }
            }
            .onAppear { play = true } // the animator runs when its trigger changes
            .task {
                try? await Task.sleep(for: .seconds(0.95))
                done()
            }
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
                if entry.provider == PollinationsClient.provider {
                    AICredit().padding(.leading, 4)
                } else {
                    Credit(name: entry.photographerName, photographer: entry.photographerURL, page: entry.photoPageURL)
                        .padding(.leading, 4)
                }
            }
            Spacer(minLength: 0)
            HStack(spacing: 8) {
                if !app.accentOptions.isEmpty {
                    AccentPill().padding(.trailing, -6) // 2 pt from Share
                }
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

/// "Made with Pollinations AI" for an AI picture (no "on Unsplash").
struct AICredit: View {
    var body: some View {
        HStack(spacing: 2) {
            Text("Made with").foregroundStyle(Color.muted)
            Button("Pollinations AI") { NSWorkspace.shared.open(URL(string: "https://pollinations.ai")!) }
                .buttonStyle(TextLinkStyle())
                .fontWeight(.semibold)
                .padding(.horizontal, 3)
        }
        .font(.system(size: 13))
        .lineLimit(1)
    }
}

/// The photo's three colors in one small pill; the chosen one (ringed) is the accent on the account's other devices.
private struct AccentPill: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        HStack(spacing: 1) {
            ForEach(app.accentOptions, id: \.self) { hex in
                Swatch(hex: hex, chosen: app.chosenAccent?.caseInsensitiveCompare(hex) == .orderedSame) { app.chooseAccent(hex) }
            }
        }
        .padding(.horizontal, 5)
        .frame(height: 36)
        .background(Color.surface, in: Capsule())
    }

    private struct Swatch: View {
        var hex: String
        var chosen: Bool
        var choose: () -> Void
        @State private var hover = false

        var body: some View {
            Button(action: choose) {
                ZStack {
                    Circle()
                        .strokeBorder(Color.ink, lineWidth: 1.75)
                        .opacity(chosen ? 1 : 0)
                        .animation(.easeOut(duration: 0.18), value: chosen)
                    Circle().fill(Color(hex: UInt32(hex.dropFirst(), radix: 16) ?? 0)).frame(width: 16, height: 16)
                }
                .frame(width: 24, height: 24)
                .contentShape(Circle())
            }
            .buttonStyle(SwatchPress(hover: hover))
            .onHover { hover = $0 }
            .help("Use as accent color")
            .accessibilityLabel("Accent color \(hex)")
            .accessibilityAddTraits(chosen ? .isSelected : [])
        }
    }

    /// No box behind a dot: it grows under the pointer and shrinks a little when pressed.
    private struct SwatchPress: ButtonStyle {
        var hover: Bool

        func makeBody(configuration: Configuration) -> some View {
            configuration.label
                .scaleEffect(configuration.isPressed ? 0.92 : hover ? 1.15 : 1)
                .animation(.easeOut(duration: 0.12), value: hover)
                .animation(.easeOut(duration: 0.08), value: configuration.isPressed)
        }
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
