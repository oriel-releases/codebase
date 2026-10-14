import AppKit
import ImageIO
import Synchronization
import UniformTypeIdentifiers

/// Sets wallpapers with NSWorkspace, one screen at a time. macOS only changes the current Space, so the setter
/// remembers what it set and `reapplyIfNeeded()` (called when the Space changes) puts it on the other Spaces too.
public final class MacWallpaperSetter: WallpaperSetter {
    /// Where the pieces of a spanned photo go (rewritten for each new photo).
    private let spanFolder: URL
    /// What Oriel set on each screen during this run: screen ID -> (file, fit).
    private let applied = Mutex<[String: Applied]>([:])

    struct Applied: Sendable {
        var file: URL
        var fit: WallpaperFit
    }

    public init(paths: AppPaths) {
        spanFolder = paths.dataRoot.appending(path: "span", directoryHint: .isDirectory)
    }

    // MARK: - WallpaperSetter

    public func screens() -> [ScreenInfo] {
        onMain { Self.screenList().map(\.info) }
    }

    public func setForAll(_ file: URL, fit: WallpaperFit) throws {
        try onMain {
            for screen in Self.screenList() { try self.apply(file, fit: fit, to: screen) }
        }
    }

    public func set(_ file: URL, on screenID: String, fit: WallpaperFit) throws {
        try onMain {
            guard let screen = Self.screenList().first(where: { $0.info.id == screenID }) else {
                throw WallpaperError("That screen isn't connected any more.")
            }
            try self.apply(file, fit: fit, to: screen)
        }
    }

    public func setSpanning(_ file: URL) throws {
        let screens = onMain { Self.screenList().map(\.info) }
        let pieces = try cut(file, for: screens)
        try onMain {
            for screen in Self.screenList() {
                if let piece = pieces[screen.info.id] { try self.apply(piece, fit: .fill, to: screen) }
            }
        }
    }

    public func current(on screenID: String?) -> URL? {
        onMain {
            let urls = Self.screenList().map { (id: $0.info.id, url: NSWorkspace.shared.desktopImageURL(for: $0.screen)) }
            if let screenID { return urls.first { $0.id == screenID }?.url }
            let distinct = Set(urls.compactMap { $0.url?.standardizedFileURL })
            return distinct.count == 1 ? distinct.first : nil
        }
    }

    // MARK: - Spaces

    /// Puts Oriel's photo back on screens that show something else: a new Space, or one Oriel hadn't visited.
    /// A picture the user chose themselves (anything outside Oriel's folders and the system pictures) is left alone.
    @MainActor
    public func reapplyIfNeeded(isOrielFile: (URL) -> Bool) {
        let remembered = applied.withLock { $0 }
        guard !remembered.isEmpty else { return }
        for screen in Self.screenList() {
            guard let want = remembered[screen.info.id] else { continue }
            let have = NSWorkspace.shared.desktopImageURL(for: screen.screen)
            if have?.standardizedFileURL == want.file.standardizedFileURL { continue }
            if let have, !isOrielFile(have) && !Self.isSystemPicture(have) { continue }
            try? apply(want.file, fit: want.fit, to: screen)
        }
    }

    /// Remembers the photo on screen now (at launch), so Spaces get it too without a new change.
    public func adopt(_ file: URL, fit: WallpaperFit) {
        let ids = screens().map(\.id)
        applied.withLock { state in
            for id in ids where state[id] == nil { state[id] = Applied(file: file, fit: fit) }
        }
    }

    static func isSystemPicture(_ url: URL) -> Bool {
        let path = url.path(percentEncoded: false)
        return path.hasPrefix("/System/Library/Desktop Pictures") || path.hasPrefix("/Library/Desktop Pictures")
    }

    // MARK: - Internals

    struct Screen {
        var screen: NSScreen
        var info: ScreenInfo
    }

    /// Connected screens with their frames in pixels, top-left origin, at the sharpest screen's scale.
    @MainActor
    static func screenList() -> [Screen] {
        let screens = NSScreen.screens
        guard let main = screens.first else { return [] }
        let scale = screens.map(\.backingScaleFactor).max() ?? 2
        return screens.map { screen in
            let f = screen.frame
            let frame = PixelRect(x: Int((f.minX * scale).rounded()),
                                  y: Int(((main.frame.maxY - f.maxY) * scale).rounded()),
                                  width: Int((f.width * scale).rounded()),
                                  height: Int((f.height * scale).rounded()))
            return Screen(screen: screen, info: ScreenInfo(id: id(of: screen), frame: frame, isMain: screen == main))
        }
    }

    /// The display's UUID: stable across restarts and reconnects.
    @MainActor
    static func id(of screen: NSScreen) -> String {
        let number = (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? 0
        if let uuid = CGDisplayCreateUUIDFromDisplayID(number)?.takeRetainedValue(),
           let text = CFUUIDCreateString(nil, uuid) as String? {
            return text
        }
        return String(number)
    }

    @MainActor
    private func apply(_ file: URL, fit: WallpaperFit, to screen: Screen) throws {
        try NSWorkspace.shared.setDesktopImageURL(file, for: screen.screen, options: Self.options(fit))
        applied.withLock { $0[screen.info.id] = Applied(file: file, fit: fit) }
    }

    static func options(_ fit: WallpaperFit) -> [NSWorkspace.DesktopImageOptionKey: Any] {
        let (scaling, clip): (NSImageScaling, Bool) = switch fit {
        case .fill, .span: (.scaleProportionallyUpOrDown, true)
        case .fit: (.scaleProportionallyUpOrDown, false)
        case .stretch: (.scaleAxesIndependently, true)
        case .center: (.scaleNone, true)
        }
        return [.imageScaling: scaling.rawValue, .allowClipping: clip, .fillColor: NSColor.black]
    }

    /// Cuts a photo sized for the whole desktop into one piece per screen.
    private func cut(_ file: URL, for screens: [ScreenInfo]) throws -> [String: URL] {
        guard let source = CGImageSourceCreateWithURL(file as CFURL, nil),
              let image = CGImageSourceCreateImageAtIndex(source, 0, nil), !screens.isEmpty else {
            throw WallpaperError("Couldn't read the photo to spread it across your screens.")
        }
        let left = screens.map(\.frame.x).min()!, top = screens.map(\.frame.y).min()!
        let width = screens.map { $0.frame.x + $0.frame.width }.max()! - left
        let height = screens.map { $0.frame.y + $0.frame.height }.max()! - top
        let sx = Double(image.width) / Double(width), sy = Double(image.height) / Double(height)

        let fm = FileManager.default
        try? fm.removeItem(at: spanFolder) // the previous photo's pieces
        try fm.createDirectory(at: spanFolder, withIntermediateDirectories: true)

        var pieces: [String: URL] = [:]
        for (i, screen) in screens.enumerated() {
            let rect = CGRect(x: Double(screen.frame.x - left) * sx, y: Double(screen.frame.y - top) * sy,
                              width: Double(screen.frame.width) * sx, height: Double(screen.frame.height) * sy).integral
            guard let piece = image.cropping(to: rect) else { continue }
            // A new name every time: macOS keeps showing a cached picture when the path stays the same.
            let url = spanFolder.appending(path: "\(file.deletingPathExtension().lastPathComponent)_\(i).jpg")
            guard let dest = CGImageDestinationCreateWithURL(url as CFURL, UTType.jpeg.identifier as CFString, 1, nil) else { continue }
            CGImageDestinationAddImage(dest, piece, [kCGImageDestinationLossyCompressionQuality: 0.92] as CFDictionary)
            if CGImageDestinationFinalize(dest) { pieces[screen.id] = url }
        }
        return pieces
    }
}

/// Runs AppKit work on the main thread, from wherever the caller is.
func onMain<T: Sendable>(_ body: @MainActor () throws -> T) rethrows -> T {
    if Thread.isMainThread { return try MainActor.assumeIsolated(body) }
    return try DispatchQueue.main.sync { try MainActor.assumeIsolated(body) }
}
