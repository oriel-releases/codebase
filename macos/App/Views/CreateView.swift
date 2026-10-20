import OrielKit
import SwiftUI

/// The AI palette: elegant, not a rainbow.
enum AIColors {
    static let indigo = Color(hex: 0x5B5BF7)
    static let violet = Color(hex: 0x8B5CF6)
    static let orchid = Color(hex: 0xC084FC)
    static let softBlue = Color(hex: 0x60A5FA)

    /// The ring around the words.
    static let ring = AngularGradient(stops: [
        .init(color: indigo, location: 0), .init(color: violet, location: 0.4),
        .init(color: orchid, location: 0.7), .init(color: softBlue, location: 0.9), .init(color: indigo, location: 1),
    ], center: .center)

    /// The sparkles icons.
    static let text = LinearGradient(stops: [
        .init(color: indigo, location: 0), .init(color: violet, location: 0.55), .init(color: orchid, location: 1),
    ], startPoint: .leading, endPoint: .trailing)
}

/// AI wallpapers: a few words and a look become a full wallpaper, shown first, set when asked. Compact: no title, no
/// explanations. It's a wallpaper app.
struct CreateView: View {
    @Environment(AppModel.self) private var app
    @FocusState private var fieldFocused: Bool

    var body: some View {
        let create = app.create
        ZStack {
            Bubbles()
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    HStack(spacing: 12) {
                        PromptBar(focused: $fieldFocused)
                        Button {
                            Task { await create.lucky() }
                        } label: {
                            Label("I'm feeling lucky", systemImage: "die.face.5")
                                .font(.system(size: 13, weight: .medium))
                                .padding(.horizontal, 18)
                                .frame(height: 56)
                                .background(Color.surface, in: Capsule())
                                .contentShape(Capsule())
                        }
                        .buttonStyle(.plain)
                        .foregroundStyle(Color.ink)
                        .disabled(create.isCreating || !create.isConfigured)
                    }
                    Looks()
                    if !create.isConfigured {
                        InfoBar(banner: Banner(severity: .warning, title: "", message: "This build of Oriel can't create AI wallpapers.", closable: false))
                    } else if let banner = create.banner {
                        InfoBar(banner: banner) { create.banner = nil }
                    }
                    Canvas(focusField: { fieldFocused = true })
                }
                .pageColumn(top: 32)
            }
        }
        .onAppear { create.shuffle() } // the ideas change on every visit
        .onChange(of: create.focusField) { fieldFocused = true }
    }
}

/// The words, in a field with a slowly turning gradient ring, and Create.
private struct PromptBar: View {
    @Environment(AppModel.self) private var app
    var focused: FocusState<Bool>.Binding
    @State private var turn = 0.0
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        @Bindable var create = app.create
        HStack(spacing: 10) {
            Image(systemName: "sparkles")
                .font(.system(size: 16))
                .foregroundStyle(AIColors.text)
            TextField("Describe a wallpaper, like a cabin in the snow", text: $create.text)
                .textFieldStyle(.plain)
                .font(.system(size: 15))
                .focused(focused)
                .onSubmit { Task { await create.create() } }
            Button {
                Task { await create.create() }
            } label: {
                HStack(spacing: 6) {
                    if create.isCreating { ProgressView().controlSize(.small).tint(Color.paper) }
                    Text("Create")
                }
                .padding(.horizontal, 6)
            }
            .buttonStyle(PrimaryButtonStyle(height: 40))
            .clipShape(Capsule())
            .disabled(create.isCreating || !create.isConfigured || create.text.trimmingCharacters(in: .whitespaces).isEmpty)
            .help("\(create.leftToday) of \(PollinationsClient.maxPerDay) left today")
        }
        .padding(.leading, 18)
        .padding(.trailing, 6)
        .frame(height: 52)
        .background(Color.paper, in: Capsule())
        .padding(2)
        .background {
            // The ring: the AI gradient, turning once every 6 s.
            GeometryReader { g in
                let side = max(g.size.width, g.size.height) * 1.5
                AIColors.ring
                    .frame(width: side, height: side)
                    .rotationEffect(.degrees(turn))
                    .position(x: g.size.width / 2, y: g.size.height / 2)
            }
            .clipShape(Capsule())
        }
        .onAppear {
            guard !reduceMotion else { return }
            withAnimation(.linear(duration: 6).repeatForever(autoreverses: false)) { turn = 360 }
        }
        .onDisappear { turn = 0 }
    }
}

/// The looks: exactly one is on.
private struct Looks: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        let create = app.create
        FlowLayout(spacing: 8) {
            ForEach(CreateIdeas.styles, id: \.self) { style in
                let on = create.style == style
                Button {
                    create.style = style
                } label: {
                    Text(style.label)
                        .font(.system(size: 13, weight: on ? .semibold : .regular))
                        .padding(.horizontal, 14)
                        .padding(.vertical, 6)
                        .foregroundStyle(on ? Color.paper : Color.ink)
                        .background(on ? Color.ink : Color.surface, in: Capsule())
                        .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
    }
}

/// Ideas, then the making, then the picture.
private struct Canvas: View {
    @Environment(AppModel.self) private var app
    var focusField: () -> Void

    var body: some View {
        let create = app.create
        ZStack {
            Color.surface
            if create.isCreating {
                Making(step: create.step)
            } else if let photo = create.photo {
                Picture(photo: photo)
            } else {
                Ideas(focusField: focusField)
            }
        }
        .frame(height: 430)
        .clipShape(.rounded(Radius.large))
    }
}

private struct Ideas: View {
    @Environment(AppModel.self) private var app
    var focusField: () -> Void

    var body: some View {
        let create = app.create
        VStack(spacing: 18) {
            FlowLayout(spacing: 8) {
                ForEach(create.ideas, id: \.self) { idea in
                    Button {
                        create.use(idea)
                        focusField()
                    } label: {
                        Text(idea.capitalizedFirst)
                            .font(.system(size: 13))
                            .padding(.horizontal, 12)
                            .padding(.vertical, 7)
                            .background(Color.paper, in: .rounded(Radius.pill))
                    }
                    .buttonStyle(.plain)
                    .foregroundStyle(Color.ink)
                }
            }
            .frame(maxWidth: 620)
            Button {
                create.shuffle()
            } label: {
                Label("Shuffle", systemImage: "shuffle")
            }
            .buttonStyle(PillButtonStyle())
        }
        .padding(32)
    }
}

/// A breathing, turning sparkles icon over the step words.
private struct Making: View {
    var step: String
    @State private var breathe = false
    @State private var sway = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        VStack(spacing: 16) {
            Image(systemName: "sparkles")
                .font(.system(size: 34))
                .foregroundStyle(AIColors.text)
                .scaleEffect(reduceMotion ? 1 : breathe ? 1.14 : 0.86)
                .rotationEffect(.degrees(reduceMotion ? 0 : sway ? 12 : -12))
            SlideText(text: step)
                .font(.system(size: 14, weight: .semibold))
                .foregroundStyle(Color.ink)
        }
        .onAppear {
            guard !reduceMotion else { return }
            withAnimation(.easeInOut(duration: 0.9).repeatForever()) { breathe = true }
            withAnimation(.easeInOut(duration: 1.8).repeatForever()) { sway = true }
        }
    }
}

/// The new picture, with the user's words and Set as wallpaper on the scrim.
private struct Picture: View {
    @Environment(AppModel.self) private var app
    var photo: PhotoCandidate
    @State private var shown = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let create = app.create
        ZStack(alignment: .bottom) {
            Color.surface
                .overlay {
                    if let image = create.preview {
                        // The same reveal every photo has: from 1.1× and transparent to 1× and opaque.
                        Image(nsImage: image)
                            .resizable()
                            .scaledToFill()
                            .opacity(shown ? 1 : 0)
                            .scaleEffect(shown || reduceMotion ? 1 : 1.1)
                            .onAppear {
                                withAnimation(.timingCurve(0.2, 0.8, 0.2, 1, duration: 0.45)) { shown = true }
                            }
                    }
                }
                .clipped()
            Scrim().frame(height: 110).allowsHitTesting(false)
            HStack {
                Text(photo.description ?? "")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(.white)
                    .lineLimit(1)
                Spacer(minLength: 12)
                Button {
                    Task { await create.set() }
                } label: {
                    HStack(spacing: 6) {
                        if create.setWords != nil { ProgressView().controlSize(.mini) }
                        SlideText(text: create.setDone ? "Wallpaper set ✓" : create.setWords ?? "Set as wallpaper")
                    }
                }
                .buttonStyle(OnPhotoPillStyle())
                .disabled(create.setWords != nil || create.setDone)
            }
            .padding(EdgeInsets(top: 14, leading: 18, bottom: 14, trailing: 14))
        }
    }
}

/// Words that change slide up into place and fade in (from 9 pt below, 260 ms, ease out).
struct SlideText: View {
    var text: String
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ZStack {
            Text(text)
                .id(text)
                .transition(reduceMotion ? .opacity : .offset(y: 9).combined(with: .opacity))
        }
        .animation(.easeOut(duration: 0.26), value: text)
        .clipped()
    }
}

/// Four soft glows drifting and swelling behind the page, each on its own path and pace.
private struct Bubbles: View {
    struct Glow {
        var x: CGFloat, y: CGFloat, size: CGFloat
        var color: Color, alpha: Double
        var dx: CGFloat, dy: CGFloat, t: Double
    }

    static let glows = [
        Glow(x: -80, y: -60, size: 440, color: AIColors.indigo, alpha: 0x70 / 255, dx: 160, dy: 90, t: 9),
        Glow(x: 560, y: -100, size: 400, color: AIColors.violet, alpha: 0x66 / 255, dx: -140, dy: 120, t: 11),
        Glow(x: 100, y: 360, size: 380, color: AIColors.softBlue, alpha: 0x5A / 255, dx: 150, dy: -100, t: 8),
        Glow(x: 660, y: 300, size: 440, color: AIColors.orchid, alpha: 0x5A / 255, dx: -170, dy: -80, t: 12),
    ]

    var body: some View {
        ZStack(alignment: .topLeading) {
            ForEach(0..<Self.glows.count, id: \.self) { i in GlowView(glow: Self.glows[i]) }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }

    private struct GlowView: View {
        var glow: Glow
        @State private var moveX = false
        @State private var moveY = false
        @State private var swell = false
        @Environment(\.accessibilityReduceMotion) private var reduceMotion

        var body: some View {
            Circle()
                .fill(RadialGradient(stops: [
                    .init(color: glow.color.opacity(glow.alpha), location: 0),
                    .init(color: glow.color.opacity(glow.alpha * 0.4), location: 0.5),
                    .init(color: glow.color.opacity(0), location: 1),
                ], center: .center, startRadius: 0, endRadius: glow.size / 2))
                .frame(width: glow.size, height: glow.size)
                .scaleEffect(swell ? 1.18 : 1)
                .offset(x: glow.x + (moveX ? glow.dx : 0), y: glow.y + (moveY ? glow.dy : 0))
                .onAppear {
                    guard !reduceMotion else { return }
                    withAnimation(.easeInOut(duration: glow.t).repeatForever()) { moveX = true }
                    withAnimation(.easeInOut(duration: glow.t * 0.75).repeatForever()) { moveY = true }
                    withAnimation(.easeInOut(duration: glow.t * 0.6).repeatForever()) { swell = true }
                }
        }
    }
}
