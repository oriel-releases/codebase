import AppKit
import ImageIO
import SwiftUI

/// Oriel's look, shared with the website: ink on paper, softly rounded, no borders. Black and white; the only color
/// is the photos and the gold favorite star. Follows the system light/dark appearance.
extension Color {
    static let paper = Color(light: 0xFAFAFA, dark: 0x0F0F0F)
    static let surface = Color(light: 0xF1F1F1, dark: 0x1A1A1A)
    static let line = Color(light: 0xE4E4E4, dark: 0x262626)
    static let muted = Color(light: 0x6B6B6B, dark: 0x9A9A9A)
    static let ink = Color(light: 0x141414, dark: 0xF2F2F2)
    static let inkHover = Color(light: 0x363636, dark: 0xD6D6D6)
    static let star = Color(hex: 0xF5B301)
    /// Interest tiles and the Following card: always dark.
    static let tile = Color(hex: 0x1C1C1C)

    init(hex: UInt32, opacity: Double = 1) {
        self.init(.sRGB, red: Double(hex >> 16 & 0xFF) / 255, green: Double(hex >> 8 & 0xFF) / 255,
                  blue: Double(hex & 0xFF) / 255, opacity: opacity)
    }

    init(light: UInt32, dark: UInt32) {
        self.init(nsColor: NSColor(name: nil) { appearance in
            let hex = appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? dark : light
            return NSColor(srgbRed: CGFloat(hex >> 16 & 0xFF) / 255, green: CGFloat(hex >> 8 & 0xFF) / 255,
                           blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
        })
    }
}

enum Radius {
    static let pill: CGFloat = 12
    static let icon: CGFloat = 18
    static let tabItem: CGFloat = 9
    static let control: CGFloat = 8
    static let medium: CGFloat = 12
    static let card: CGFloat = 16
    static let large: CGFloat = 20
    static let thumb: CGFloat = 8
    static let bar: CGFloat = 26
}

extension Shape where Self == RoundedRectangle {
    static func rounded(_ r: CGFloat) -> RoundedRectangle { RoundedRectangle(cornerRadius: r, style: .continuous) }
}

/// The bottom of photo cards, so white text stays readable.
struct Scrim: View {
    var body: some View {
        LinearGradient(stops: [
            .init(color: .clear, location: 0),
            .init(color: .black.opacity(0x59 / 255), location: 0.45),
            .init(color: .black.opacity(0xCC / 255), location: 1),
        ], startPoint: .top, endPoint: .bottom)
    }
}

// MARK: - Type

extension View {
    func pageTitle() -> some View {
        font(.system(size: 30, weight: .semibold)).tracking(-0.6).foregroundStyle(Color.ink)
    }

    func sectionHeader() -> some View {
        font(.system(size: 15, weight: .semibold)).foregroundStyle(Color.ink)
    }

    /// Page content: at most 960 wide, centered, with the page margins.
    func pageColumn(top: CGFloat = 8) -> some View {
        frame(maxWidth: 960, alignment: .leading)
            .padding(.horizontal, 40)
            .padding(.top, top)
            .padding(.bottom, 40)
            .frame(maxWidth: .infinity)
    }
}

// MARK: - Buttons

/// Plain pill: `surface`, `line` on hover, no border.
struct PillButtonStyle: ButtonStyle {
    var height: CGFloat? = nil
    var minWidth: CGFloat? = nil

    func makeBody(configuration: Configuration) -> some View {
        Styled(configuration: configuration, height: height, minWidth: minWidth)
    }

    private struct Styled: View {
        let configuration: Configuration
        let height: CGFloat?
        let minWidth: CGFloat?
        @State private var hover = false
        @Environment(\.isEnabled) private var enabled

        var body: some View {
            configuration.label
                .font(.system(size: 13, weight: .medium))
                .padding(.horizontal, 16)
                .padding(.vertical, 7)
                .frame(minWidth: minWidth, minHeight: height)
                .foregroundStyle(enabled ? Color.ink : Color.muted)
                .background(hover || configuration.isPressed ? Color.line : Color.surface, in: .rounded(Radius.pill))
                .contentShape(.rounded(Radius.pill))
                .onHover { hover = $0 }
        }
    }
}

/// Primary pill: `ink` background, `paper` text.
struct PrimaryButtonStyle: ButtonStyle {
    var height: CGFloat? = nil
    var minWidth: CGFloat? = nil

    func makeBody(configuration: Configuration) -> some View {
        Styled(configuration: configuration, height: height, minWidth: minWidth)
    }

    private struct Styled: View {
        let configuration: Configuration
        let height: CGFloat?
        let minWidth: CGFloat?
        @State private var hover = false
        @Environment(\.isEnabled) private var enabled

        var body: some View {
            configuration.label
                .font(.system(size: 13, weight: .semibold))
                .padding(.horizontal, 16)
                .padding(.vertical, 7)
                .frame(minWidth: minWidth, minHeight: height)
                .foregroundStyle(Color.paper)
                .background(hover || configuration.isPressed ? Color.inkHover : Color.ink, in: .rounded(Radius.pill))
                .opacity(enabled ? 1 : 0.45)
                .contentShape(.rounded(Radius.pill))
                .onHover { hover = $0 }
        }
    }
}

/// A 36×36 circle icon button.
struct IconButtonStyle: ButtonStyle {
    var size: CGFloat = 36

    func makeBody(configuration: Configuration) -> some View {
        Styled(configuration: configuration, size: size)
    }

    private struct Styled: View {
        let configuration: Configuration
        let size: CGFloat
        @State private var hover = false
        @Environment(\.isEnabled) private var enabled

        var body: some View {
            configuration.label
                .font(.system(size: 14))
                .frame(width: size, height: size)
                .foregroundStyle(enabled ? Color.ink : Color.muted)
                .background(hover || configuration.isPressed ? Color.line : Color.surface, in: Circle())
                .contentShape(Circle())
                .onHover { hover = $0 }
        }
    }
}

/// The light pill on photos (Search's "Set as wallpaper").
struct OnPhotoPillStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        Styled(configuration: configuration)
    }

    private struct Styled: View {
        let configuration: Configuration
        @State private var hover = false

        var body: some View {
            configuration.label
                .font(.system(size: 13, weight: .medium))
                .padding(.horizontal, 14)
                .padding(.vertical, 5)
                .foregroundStyle(Color(hex: 0x141414))
                .background(Color(hex: configuration.isPressed ? 0xD0D0D0 : hover ? 0xE0E0E0 : 0xF2F2F2), in: .rounded(Radius.pill))
                .onHover { hover = $0 }
        }
    }
}

/// White-on-glass icon buttons on photos (History's card actions).
struct OnPhotoIconStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        Styled(configuration: configuration)
    }

    private struct Styled: View {
        let configuration: Configuration
        @State private var hover = false
        @Environment(\.isEnabled) private var enabled

        var body: some View {
            configuration.label
                .font(.system(size: 13))
                .frame(width: 30, height: 30)
                .foregroundStyle(.white.opacity(enabled ? 1 : 0.5))
                .background(.white.opacity(!enabled ? 0.1 : configuration.isPressed ? 0.4 : hover ? 0.3 : 0.18), in: Circle())
                .onHover { hover = $0 }
        }
    }
}

/// A link that looks like text (photographer names).
struct TextLinkStyle: ButtonStyle {
    var color: Color = .ink

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(color.opacity(configuration.isPressed ? 0.7 : 1))
            .contentShape(Rectangle())
    }
}

// MARK: - Banner

/// Windows' InfoBar: a rounded banner with a severity icon, bold title, message and optional close.
struct InfoBar: View {
    var banner: Banner
    var onClose: (() -> Void)? = nil

    private var tint: Color {
        switch banner.severity {
        case .info: .blue
        case .success: .green
        case .warning: .yellow
        case .error: .red
        }
    }

    private var icon: String {
        switch banner.severity {
        case .info: "info.circle.fill"
        case .success: "checkmark.circle.fill"
        case .warning: "exclamationmark.triangle.fill"
        case .error: "xmark.octagon.fill"
        }
    }

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Image(systemName: icon).foregroundStyle(tint)
            Text("\(Text(banner.title.isEmpty ? "" : banner.title + "  ").bold())\(banner.message)")
            Spacer(minLength: 0)
            if banner.closable, let onClose {
                Button(action: onClose) { Image(systemName: "xmark").font(.system(size: 11, weight: .semibold)) }
                    .buttonStyle(.plain)
                    .foregroundStyle(Color.muted)
                    .accessibilityLabel("Close")
            }
        }
        .font(.system(size: 13))
        .foregroundStyle(Color.ink)
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.surface, in: .rounded(Radius.medium))
        .background(tint.opacity(0.12), in: .rounded(Radius.medium))
        .transition(.opacity)
    }
}

// MARK: - Setting row

/// A settings row: icon, title and description, the control on the right.
struct SettingRow<Control: View>: View {
    var icon: String
    var title: String
    var description: String = ""
    @ViewBuilder var control: Control

    var body: some View {
        HStack(spacing: 16) {
            Image(systemName: icon)
                .font(.system(size: 18))
                .foregroundStyle(Color.muted)
                .frame(width: 24)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.system(size: 14)).foregroundStyle(Color.ink)
                if !description.isEmpty {
                    Text(description).font(.system(size: 12)).foregroundStyle(Color.muted).fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 12)
            control
        }
        .padding(EdgeInsets(top: 14, leading: 20, bottom: 14, trailing: 16))
        .frame(minHeight: 64)
        .background(Color.surface, in: .rounded(Radius.card))
    }
}

// MARK: - Photos

/// Decodes photos at the size they're shown, off the main thread, and keeps recent ones in memory.
@MainActor
enum ImageCache {
    private static let cache: NSCache<NSString, NSImage> = {
        let c = NSCache<NSString, NSImage>()
        c.countLimit = 300
        return c
    }()
    /// Sources already revealed once: shown at once next time (going back to a page).
    private(set) static var revealed = Set<String>()

    static func markRevealed(_ key: String) { revealed.insert(key) }

    static func cached(_ key: String) -> NSImage? { cache.object(forKey: key as NSString) }

    static func load(file: String?, url: String?, maxPixels: Int) async -> NSImage? {
        let key = "\(file ?? url ?? "")#\(maxPixels)"
        if let hit = cache.object(forKey: key as NSString) { return hit }
        var image: CGImage?
        if let file, !file.isEmpty {
            image = await Task.detached(priority: .userInitiated) { decode(URL(filePath: file), maxPixels) }.value
        }
        if image == nil, let url, let remote = URL(string: url) {
            if let (data, _) = try? await URLSession.shared.data(from: remote) {
                image = await Task.detached(priority: .userInitiated) { decode(data, maxPixels) }.value
            }
        }
        guard let image else { return nil }
        let ns = NSImage(cgImage: image, size: NSSize(width: image.width, height: image.height))
        cache.setObject(ns, forKey: key as NSString)
        return ns
    }

    nonisolated static func decode(_ url: URL, _ maxPixels: Int) -> CGImage? {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, [kCGImageSourceShouldCache: false] as CFDictionary) else { return nil }
        return thumbnail(source, maxPixels)
    }

    nonisolated static func decode(_ data: Data, _ maxPixels: Int) -> CGImage? {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
        return thumbnail(source, maxPixels)
    }

    nonisolated static func thumbnail(_ source: CGImageSource, _ maxPixels: Int) -> CGImage? {
        CGImageSourceCreateThumbnailAtIndex(source, 0, [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: maxPixels,
        ] as CFDictionary)
    }

    /// The window closed: let the photos go.
    static func release() {
        cache.removeAllObjects()
    }
}

/// A photo (local file first, else its URL) that eases in: from opacity 0 and scale 1.1 to 1/1 (opacity 450 ms,
/// scale 700 ms). One already revealed once just appears. Respects Reduce Motion.
struct Photo: View {
    var file: String? = nil
    var url: String? = nil
    var maxPixels = 600
    var placeholder: Color = .line

    @State private var image: NSImage?
    @State private var shown = false
    @State private var scaled = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var key: String { "\(file ?? url ?? "")#\(maxPixels)" }

    var body: some View {
        // The placeholder sets the size (exactly what the card gives); the photo fills it as an overlay. A fill-scaled
        // image in a ZStack would report its own, bigger size and push the card's text out of view.
        placeholder
            .overlay {
                if let image {
                    Image(nsImage: image)
                        .resizable()
                        .scaledToFill()
                        .opacity(shown ? 1 : 0)
                        .scaleEffect(scaled || reduceMotion ? 1 : 1.1)
                }
            }
            .clipped()
        .task(id: key) {
            let instant = ImageCache.revealed.contains(key)
            if instant, let hit = ImageCache.cached(key) {
                image = hit
                shown = true
                scaled = true
                return
            }
            shown = false
            scaled = false
            guard let loaded = await ImageCache.load(file: file, url: url, maxPixels: maxPixels) else { return }
            image = loaded
            ImageCache.markRevealed(key)
            if instant {
                shown = true
                scaled = true
            } else {
                withAnimation(.timingCurve(0.2, 0.8, 0.2, 1, duration: 0.45)) { shown = true }
                withAnimation(.timingCurve(0.2, 0.8, 0.2, 1, duration: 0.7)) { scaled = true }
            }
        }
    }
}

/// The Oriel logo (light and dark variants).
struct Logo: View {
    var size: CGFloat
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        Image(scheme == .dark ? "LogoDark" : "Logo")
            .resizable()
            .interpolation(.high)
            .frame(width: size, height: size)
            .accessibilityHidden(true)
    }
}

/// A word that slides: the old one goes up and fades (130 ms), the new one comes from below (130 ms).
struct SlidingText: View {
    var text: String

    @State private var shown = ""
    @State private var offset: CGFloat = 0
    @State private var opacity: Double = 1
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Text(shown)
            .offset(y: offset)
            .opacity(opacity)
            .onAppear { shown = text }
            .onChange(of: text) { _, new in
                guard !reduceMotion else {
                    shown = new
                    return
                }
                withAnimation(.easeIn(duration: 0.13)) {
                    offset = -8
                    opacity = 0
                } completion: {
                    // Only the newest word: one that arrived meanwhile wins.
                    shown = text
                    offset = 8
                    withAnimation(.easeOut(duration: 0.13)) {
                        offset = 0
                        opacity = 1
                    }
                }
            }
    }
}
