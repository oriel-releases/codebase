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
    public static func pick(_ rgba: [UInt8]) -> (r: UInt8, g: UInt8, b: UInt8) { pickTop(rgba, count: 1)[0] }

    typealias RGB = (r: UInt8, g: UInt8, b: UInt8)

    /// The photo's most used striking colors, best first, each from its own part of the color wheel when the photo has
    /// enough of them (so three blues don't fill the choice), and adjusted like `pick`. Always `count`: a photo with
    /// fewer colors gets neighboring hues, then lighter and darker shades.
    public static func pickTop(_ rgba: [UInt8], count: Int = 3) -> [(r: UInt8, g: UInt8, b: UInt8)] {
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

        let top = weight.max() ?? 0
        if top <= 0 {
            if n == 0 { return shades((0x00, 0x78, 0xD4), count) }
            // A grayscale photo: its average, in shades.
            let (h, s, v) = toHSV(allR / Double(n), allG / Double(n), allB / Double(n))
            return shades(fromHSV(h, s, v.clamped(0.55, 0.85)), count)
        }

        // Most used first; ties keep the wheel's order (like LINQ's stable OrderByDescending).
        let ranked = weight.indices.sorted { weight[$0] != weight[$1] ? weight[$0] > weight[$1] : $0 < $1 }
        var picked: [Int] = []
        for bucket in ranked {
            if picked.count == count { break }
            // A color the photo barely has isn't worth offering.
            if weight[bucket] < top * 0.04 { break }
            // Its own hue: not next to one already picked (the wheel wraps around).
            if picked.contains(where: { min(abs($0 - bucket), hueBuckets - abs($0 - bucket)) <= 1 }) { continue }
            picked.append(bucket)
        }
        // Few colors: the next most used hues, even next to one already picked.
        for bucket in ranked {
            if picked.count == count || weight[bucket] <= 0 { break }
            if !picked.contains(bucket) { picked.append(bucket) }
        }

        var colors: [RGB] = []
        for i in picked {
            let (h, s, v) = toHSV(sumR[i] / weight[i], sumG[i] / weight[i], sumB[i] / weight[i])
            let c = fromHSV(h, max(s, 0.55), v.clamped(0.55, 0.85))
            if !colors.contains(where: { $0 == c }) { colors.append(c) }
        }
        // Still short (one hue only): its lighter and darker shades.
        for shade in shades(colors[0], count).dropFirst() where colors.count < count && !colors.contains(where: { $0 == shade }) {
            colors.append(shade)
        }
        return colors
    }

    /// The color, then a darker and a lighter shade of it, and so on: `count` in all.
    static func shades(_ color: RGB, _ count: Int) -> [RGB] {
        let (h, s, v) = toHSV(Double(color.r), Double(color.g), Double(color.b))
        var list: [RGB] = [color]
        var i = 1
        while list.count < count && i <= count {
            if list.count < count { list.append(fromHSV(h, s, max(0.3, v - 0.2 * Double(i)))) }
            if list.count < count { list.append(fromHSV(h, s * 0.7, min(1, v + 0.15 * Double(i)))) }
            i += 1
        }
        return list
    }

    public static func hex(_ c: (r: UInt8, g: UInt8, b: UInt8)) -> String {
        String(format: "#%02X%02X%02X", c.r, c.g, c.b)
    }

    public static func toHSV(_ r: Double, _ g: Double, _ b: Double) -> (Double, Double, Double) {
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

/// Decodes a photo at 96×54 and picks its colors. Remembers the last one (the same photo is often asked twice).
public final class AccentSampler: Sendable {
    private let last = Mutex<(path: String, colors: [String])?>(nil)

    public init() {}

    /// The photo's accent as "#RRGGBB", or nil when it can't be read.
    public func sample(_ path: String) -> String? { sampleTop(path).first }

    /// The photo's top three colors ("#RRGGBB", best first), empty when it can't be read.
    public func sampleTop(_ path: String) -> [String] {
        if let hit = last.withLock({ $0 }), hit.path == path { return hit.colors }
        let colors = Self.decode(path).map { AccentPicker.pickTop($0).map(AccentPicker.hex) } ?? []
        last.withLock { $0 = (path, colors) }
        return colors
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
