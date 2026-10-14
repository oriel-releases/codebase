import CoreGraphics
import Foundation
import ImageIO
import Synchronization

/// Picks an accent color from a small copy of a photo (the `color` other devices get with `sync/current`).
/// Same math as Windows' AccentPicker, reading RGBA instead of BGRA.
public enum AccentPicker {
    static let hueBuckets = 24

    /// The photo's most striking color, adjusted so it works as a UI accent (white text stays readable).
    /// - Parameter rgba: pixels as R,G,B,A bytes.
    public static func pick(_ rgba: [UInt8]) -> (r: UInt8, g: UInt8, b: UInt8) {
        var weight = [Double](repeating: 0, count: hueBuckets)
        var sumR = weight, sumG = weight, sumB = weight
        var allR = 0.0, allG = 0.0, allB = 0.0
        let n = rgba.count / 4

        for i in 0..<n {
            let r = Double(rgba[i * 4]), g = Double(rgba[i * 4 + 1]), b = Double(rgba[i * 4 + 2])
            allR += r; allG += g; allB += b
            let (h, s, v) = toHSV(r, g, b)
            // Skip near-black, near-white and washed-out pixels: they make dull accents.
            if v < 0.2 || s < 0.25 || (v > 0.95 && s < 0.4) { continue }
            let bucket = min(hueBuckets - 1, Int(h / 360 * Double(hueBuckets)))
            let w = s * s * v // favor saturated, bright pixels
            weight[bucket] += w
            sumR[bucket] += r * w; sumG[bucket] += g * w; sumB[bucket] += b * w
        }

        let best = weight.indices.max { weight[$0] < weight[$1] } ?? 0
        let mr, mg, mb: Double
        if weight[best] > 0 {
            mr = sumR[best] / weight[best]; mg = sumG[best] / weight[best]; mb = sumB[best] / weight[best]
        } else if n > 0 {
            mr = allR / Double(n); mg = allG / Double(n); mb = allB / Double(n) // a grayscale photo: its average
        } else {
            return (0x00, 0x78, 0xD4)
        }

        var (hue, sat, val) = toHSV(mr, mg, mb)
        if weight[best] > 0 { sat = max(sat, 0.55) }
        val = min(max(val, 0.55), 0.85)
        return fromHSV(hue, sat, val)
    }

    public static func hex(_ c: (r: UInt8, g: UInt8, b: UInt8)) -> String {
        String(format: "#%02X%02X%02X", c.r, c.g, c.b)
    }

    static func toHSV(_ r: Double, _ g: Double, _ b: Double) -> (Double, Double, Double) {
        let r = r / 255, g = g / 255, b = b / 255
        let mx = max(r, g, b), mn = min(r, g, b), d = mx - mn
        var h: Double
        if d == 0 { h = 0 }
        else if mx == r { h = 60 * ((((g - b) / d).truncatingRemainder(dividingBy: 6)) + 6).truncatingRemainder(dividingBy: 6) }
        else if mx == g { h = 60 * ((b - r) / d + 2) }
        else { h = 60 * ((r - g) / d + 4) }
        return (h, mx == 0 ? 0 : d / mx, mx)
    }

    static func fromHSV(_ h: Double, _ s: Double, _ v: Double) -> (r: UInt8, g: UInt8, b: UInt8) {
        let c = v * s
        let x = c * (1 - abs((h / 60).truncatingRemainder(dividingBy: 2) - 1))
        let m = v - c
        let (r, g, b): (Double, Double, Double) = switch Int(h / 60) {
        case 0: (c, x, 0)
        case 1: (x, c, 0)
        case 2: (0, c, x)
        case 3: (0, x, c)
        case 4: (x, 0, c)
        default: (c, 0, x)
        }
        func byte(_ d: Double) -> UInt8 { UInt8(((d + m) * 255).clamped(0, 255).rounded()) }
        return (byte(r), byte(g), byte(b))
    }
}

extension Double {
    func clamped(_ lo: Double, _ hi: Double) -> Double { Swift.min(Swift.max(self, lo), hi) }
}

/// Decodes a photo at 96×54 and picks its accent. Remembers the last one (the same photo is often asked twice).
public final class AccentSampler: Sendable {
    private let last = Mutex<(path: String, hex: String?)?>(nil)

    public init() {}

    /// The photo's accent as "#RRGGBB", or nil when it can't be read.
    public func sample(_ path: String) -> String? {
        if let hit = last.withLock({ $0 }), hit.path == path { return hit.hex }
        let hex = Self.decode(path).map { AccentPicker.hex(AccentPicker.pick($0)) }
        last.withLock { $0 = (path, hex) }
        return hex
    }

    static func decode(_ path: String) -> [UInt8]? {
        guard !path.isEmpty, let source = CGImageSourceCreateWithURL(URL(filePath: path) as CFURL, nil),
              let image = CGImageSourceCreateThumbnailAtIndex(source, 0, [
                  kCGImageSourceCreateThumbnailFromImageAlways: true,
                  kCGImageSourceThumbnailMaxPixelSize: 160,
              ] as CFDictionary) else { return nil }
        let width = 96, height = 54
        var pixels = [UInt8](repeating: 0, count: width * height * 4)
        let drawn = pixels.withUnsafeMutableBytes { buffer -> Bool in
            guard let context = CGContext(data: buffer.baseAddress, width: width, height: height, bitsPerComponent: 8,
                                          bytesPerRow: width * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                          bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return false }
            context.interpolationQuality = .medium
            context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
            return true
        }
        return drawn ? pixels : nil
    }
}
