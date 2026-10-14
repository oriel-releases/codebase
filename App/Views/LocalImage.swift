import AppKit
import ImageIO
import SwiftUI

/// Decodes photos at the size they're shown (a 6K wallpaper as a 400 px thumbnail), off the main thread, and keeps
/// recent ones in memory.
@MainActor
enum Thumbnails {
    private static let cache: NSCache<NSString, NSImage> = {
        let c = NSCache<NSString, NSImage>()
        c.countLimit = 200
        return c
    }()

    static func load(_ path: String, maxPixels: Int) async -> NSImage? {
        let key = "\(path)#\(maxPixels)" as NSString
        if let hit = cache.object(forKey: key) { return hit }
        let decoded = await Task.detached(priority: .userInitiated) { () -> CGImage? in
            guard let source = CGImageSourceCreateWithURL(URL(filePath: path) as CFURL, nil) else { return nil }
            return CGImageSourceCreateThumbnailAtIndex(source, 0, [
                kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceCreateThumbnailWithTransform: true,
                kCGImageSourceThumbnailMaxPixelSize: maxPixels,
            ] as CFDictionary)
        }.value
        guard let decoded else { return nil }
        let image = NSImage(cgImage: decoded, size: NSSize(width: decoded.width, height: decoded.height))
        cache.setObject(image, forKey: key)
        return image
    }
}

/// A photo from disk, falling back to its Unsplash thumbnail when the file was cleaned up.
struct LocalImage: View {
    var path: String
    var fallbackURL: String? = nil
    var maxPixels = 600

    @State private var image: NSImage?
    @State private var missing = false

    var body: some View {
        ZStack {
            if let image {
                Image(nsImage: image)
                    .resizable()
                    .scaledToFill()
                    .transition(.opacity)
            } else if missing, let fallbackURL, let url = URL(string: fallbackURL) {
                AsyncImage(url: url) { $0.resizable().scaledToFill() } placeholder: { Rectangle().fill(.quaternary) }
            } else {
                Rectangle().fill(.quaternary)
            }
        }
        .task(id: path) {
            let loaded = await Thumbnails.load(path, maxPixels: maxPixels)
            withAnimation(.easeOut(duration: 0.25)) {
                image = loaded
                missing = loaded == nil
            }
        }
    }
}
