import Foundation

/// Puts photos on the desktop. The macOS implementation (NSWorkspace) arrives in M2; tests use a fake.
public protocol WallpaperSetter: Sendable {
    /// Connected screens, in physical pixels.
    func screens() -> [ScreenInfo]

    /// The same photo on every screen.
    func setForAll(_ file: URL, fit: WallpaperFit) throws

    /// One photo on one screen.
    func set(_ file: URL, on screenID: String, fit: WallpaperFit) throws

    /// The photo on a screen now, or the one on every screen when `screenID` is nil (nil when they differ).
    func current(on screenID: String?) -> URL?
}

public extension WallpaperSetter {
    /// Every photo on screen right now, as standardized paths.
    func filesInUse() -> Set<String> {
        var inUse = Set<String>()
        if let all = current(on: nil) { inUse.insert(all.standardizedFileURL.path(percentEncoded: false)) }
        for screen in screens() {
            if let file = current(on: screen.id) { inUse.insert(file.standardizedFileURL.path(percentEncoded: false)) }
        }
        return inUse
    }
}

/// Something went wrong setting or managing wallpapers; the message is ready to show.
public struct WallpaperError: Error, LocalizedError, Sendable {
    public var message: String

    public init(_ message: String) {
        self.message = message
    }

    public var errorDescription: String? { message }
}
