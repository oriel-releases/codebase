import Foundation
import Synchronization

/// AI wallpapers made by Pollinations (https://pollinations.ai) from a few words, with the key built into the app
/// (POLLINATIONS_KEY, see scripts/configure.sh). Pollinations keeps each picture at a public link
/// (media.pollinations.ai), which is what the account's devices sync, like an Unsplash photo: other devices download it.
public final class PollinationsClient: Sendable {
    /// The `HistoryEntry.provider` of AI pictures.
    public static let provider = "ai"
    static let imagesURL = URL(string: "https://gen.pollinations.ai/v1/images/generations")!
    static let textURL = URL(string: "https://gen.pollinations.ai/v1/chat/completions")!
    static let mediaHost = "media.pollinations.ai"
    /// Writes the full picture description from the user's few words: quick, and costs next to nothing.
    static let textModel = "openai/gpt-5.4-nano"
    /// Fast, sharp and among the cheapest models.
    static let model = "zimage"
    /// The widest picture the model makes (2560 is refused with a 400).
    static let maxSide = 2048
    /// Pictures one Mac may make a day: the key is shared by everyone, so nobody uses it all up.
    public static let maxPerDay = 20

    static let expandInstructions =
        "You write prompts for an AI image model that makes desktop wallpapers. Turn the user's few words into one vivid " +
        "prompt of 60 to 90 words: the subject, the setting, the time of day and light, colors, mood, camera or art style, " +
        "and a composition that leaves calm space for desktop icons. The scene is full-bleed: it runs to every edge of the image. Describe only the scene itself; never mention paper, canvas, frames, borders, edges, margins, posters, prints or screens. Keep everything the user asked for, in their language's " +
        "meaning but written in English, and don't add people unless asked. No text, logos or watermarks in the picture. " +
        "Reply with the prompt only."

    private let http: HTTPClient
    private let keys: ApiKeyStore
    private let countFile: URL
    private let countLock = Mutex(())

    public init(http: HTTPClient = URLSession.oriel, keys: ApiKeyStore = BuiltInApiKeyStore.pollinations(), paths: AppPaths) {
        self.http = http
        self.keys = keys
        countFile = paths.dataRoot.appending(path: "created.json")
    }

    public var isConfigured: Bool { keys.hasKey }

    /// A new seed: the same words with another seed make another picture.
    public static func newSeed() -> Int { Int(Int32.random(in: 1...Int32.max)) }

    /// Pictures this Mac can still make today.
    public var leftToday: Int { max(0, Self.maxPerDay - madeToday().count) }

    /// Paints a picture for the words, in the screen's shape (at most 2048 px wide). Returns the photo record (its public
    /// link), ready to be set, and the JPEG or PNG bytes.
    /// - Parameter progress: told what is happening: "Imagining…" while the words are written out, then "Painting…".
    public func create(prompt: String, style: String?, seed: Int, screen: PixelSize,
                       progress: @escaping @Sendable (String) -> Void = { _ in }) async throws -> (photo: PhotoCandidate, image: Data) {
        guard let key = keys.key else { throw ProviderError(.missingKey, "This build of Oriel can't create AI wallpapers.") }
        if leftToday == 0 {
            throw ProviderError(.rateLimited,
                "You've created \(Self.maxPerDay) wallpapers today, the most for one day. Come back tomorrow for more.")
        }

        let size = Self.sizeFor(screen)
        let userWords = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        let words = style.map { $0.isEmpty ? userWords : "\(userWords), \($0)" } ?? userWords
        // People write a few words; the picture model draws far better from a full description.
        progress("Imagining…")
        let described = await expand(words, key: key) ?? words
        progress("Painting…")

        // Kept by Pollinations at a public link, so the account's other devices can show the very same picture.
        var request = URLRequest(url: Self.imagesURL)
        request.httpMethod = "POST"
        request.timeoutInterval = 90
        request.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: [
            "model": Self.model,
            "prompt": Self.final(described, userWords),
            "size": "\(size.width)x\(size.height)",
            "seed": seed,
            "safe": true,
            "response_format": "url",
        ] as [String: Any])

        let data: Data
        let response: HTTPURLResponse
        do {
            (data, response) = try await http.send(request)
        } catch {
            try Task.checkCancellation()
            throw ProviderError(.network, (error as? URLError)?.code == .timedOut
                ? "Pollinations is taking too long right now. Try again in a moment."
                : "Couldn't reach Pollinations. Check your connection and try again.")
        }
        guard (200..<300).contains(response.statusCode) else { throw Self.error(response.statusCode, data) }
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let link = ((object["data"] as? [[String: Any]])?.first?["url"]) as? String,
              let photo = Self.fromLink(link, prompt: userWords, size: size) else {
            throw ProviderError(.badResponse, "Pollinations didn't send back a picture. Try again.")
        }
        let bytes = try await download(photo)
        countOne()
        Log.ai.info("Created an AI wallpaper \(photo.id) (\(size.width)x\(size.height), \(bytes.count) bytes)")
        return (photo, bytes)
    }

    /// What the picture model gets: a full-bleed wallpaper of great quality, the written-out scene, and the user's own
    /// words (so nothing they asked for is lost). Only what should be there: the model can't follow "no", and naming
    /// borders or frames (even as "no border") is what made it draw white edges and rounded corners.
    static func final(_ scene: String, _ userWords: String) -> String {
        "Full-bleed widescreen wallpaper, the scene runs to every edge of the image, great quality, highly detailed. " +
            "\(trimPeriod(scene)). User's idea: \(trimPeriod(userWords))."
    }

    private static func trimPeriod(_ s: String) -> String {
        var t = s
        while t.hasSuffix(".") { t.removeLast() }
        return t
    }

    /// True for an AI wallpaper's photo ID ("ai-" and its Pollinations media id).
    public static func isAiPhoto(_ photoId: String) -> Bool { photoId.hasPrefix("ai-") }

    /// True for a link to a picture Pollinations keeps (media.pollinations.ai/{id}).
    public static func isMediaLink(_ url: String?) -> Bool {
        guard let url, let u = URL(string: url) else { return false }
        return u.scheme == "https" && u.host() == mediaHost
    }

    /// The photo record of an AI wallpaper, from its public link: one made here, or one another device of the account
    /// made (it arrives through `sync/current` or a favorite). Nil when the link isn't one of Pollinations'.
    public static func fromLink(_ link: String, prompt: String?, size: PixelSize? = nil) -> PhotoCandidate? {
        guard isMediaLink(link), let url = URL(string: link) else { return nil }
        let mediaId = url.path(percentEncoded: false).trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        guard (16...128).contains(mediaId.count), mediaId.allSatisfy(\.isHexDigit) else { return nil }
        let clean = URL(string: "https://\(mediaHost)/\(mediaId)")!
        return PhotoCandidate(
            provider: provider, id: "ai-" + mediaId, width: size?.width ?? 0, height: size?.height ?? 0, description: prompt,
            rawURL: clean, thumbnailURL: clean, photoPageURL: clean,
            photographerName: "Pollinations AI", photographerURL: URL(string: "https://pollinations.ai")!)
    }

    /// The picture behind an AI wallpaper's public link (no key needed). Pollinations keeps it 30 days after last use.
    public func download(_ photo: PhotoCandidate) async throws -> Data {
        var request = URLRequest(url: photo.rawURL)
        request.timeoutInterval = 40
        let data: Data
        let response: HTTPURLResponse
        do {
            (data, response) = try await http.send(request)
        } catch {
            try Task.checkCancellation()
            throw ProviderError(.network, "Couldn't download the AI wallpaper. Check your connection.")
        }
        if response.statusCode == 404 { throw ProviderError(.noResults, "This AI wallpaper isn't kept online any more.") }
        guard (200..<300).contains(response.statusCode) else {
            throw ProviderError(.badResponse, "Pollinations had a problem. Try again in a moment.")
        }
        guard Self.isImage(data) else { throw ProviderError(.badResponse, "Pollinations didn't send back a picture. Try again.") }
        return data
    }

    /// The words written out as a full picture description (60 to 90 words), or nil when that didn't work.
    private func expand(_ words: String, key: String) async -> String? {
        var request = URLRequest(url: Self.textURL)
        request.httpMethod = "POST"
        request.timeoutInterval = 20
        request.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? JSONSerialization.data(withJSONObject: [
            "model": Self.textModel,
            "max_tokens": 400,
            "messages": [
                ["role": "system", "content": Self.expandInstructions],
                ["role": "user", "content": words],
            ],
        ] as [String: Any])
        do {
            let (data, response) = try await http.send(request)
            guard (200..<300).contains(response.statusCode) else {
                Log.ai.info("Couldn't write out the prompt (\(response.statusCode)); using the words as they are")
                return nil
            }
            guard let object = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let message = ((object["choices"] as? [[String: Any]])?.first?["message"]) as? [String: Any],
                  let content = message["content"] as? String else { return nil }
            let text = content.trimmingCharacters(in: .whitespacesAndNewlines).trimmingCharacters(in: CharacterSet(charactersIn: "\""))
            // Long enough to be a description, short enough to fit.
            guard text.count > 20 else { return nil }
            return String(text.prefix(1200))
        } catch {
            Log.ai.info("Couldn't write out the prompt (\(error.localizedDescription)); using the words as they are")
            return nil
        }
    }

    /// The most pixels the model makes (2048 × 1152): a 16:10 Mac screen at 2048 wide (2048 × 1328) is refused.
    static let maxPixels = 2_359_296

    /// The screen's shape, at most 2048 wide and at most `maxPixels` in all, in steps of 16 (what the models want).
    public static func sizeFor(_ screen: PixelSize) -> PixelSize {
        let w = Double(max(screen.width, 640)), h = Double(max(screen.height, 360))
        let scale = min(1, Double(maxSide) / max(w, h), (Double(maxPixels) / (w * h)).squareRoot())
        var size = PixelSize(width: round16(w * scale), height: round16(h * scale))
        // Rounding up to 16 can tip it over the limit: take 16 off the longer side until it fits.
        while size.width * size.height > maxPixels {
            if size.width >= size.height { size.width -= 16 } else { size.height -= 16 }
        }
        return size
    }

    static func round16(_ v: Double) -> Int { max(16, Int((v / 16).rounded()) * 16) }

    public static func isImage(_ b: Data) -> Bool { b.count > 1024 && (isJPEG(b) || isPNG(b)) }
    public static func isJPEG(_ b: Data) -> Bool { b.starts(with: [0xFF, 0xD8]) }
    public static func isPNG(_ b: Data) -> Bool { b.starts(with: [0x89, 0x50, 0x4E, 0x47]) }

    /// What Pollinations says went wrong, in words for the user.
    static func error(_ status: Int, _ data: Data) -> ProviderError {
        let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        let reason = object?["error"] as? String ?? (object?["error"] as? [String: Any])?["message"] as? String
        Log.ai.warning("Pollinations said \(status): \(reason ?? "")")
        return switch status {
        case 401, 402, 403: ProviderError(.rateLimited, "Creating is resting for now. Try again later.")
        case 429: ProviderError(.rateLimited, "That was a lot of pictures in a short time. Wait a minute and try again.")
        case 400: ProviderError(.badResponse, "Pollinations couldn't make that. Try other words.")
        default: ProviderError(.badResponse, "Pollinations had a problem. Try again in a moment.")
        }
    }

    // MARK: - How many were made today (this Mac, local day)

    private func madeToday() -> [Date] {
        countLock.withLock { _ in
            guard let data = try? Data(contentsOf: countFile),
                  let all = try? JSONSettingsStore.decoder.decode([Date].self, from: data) else { return [] }
            return all.filter { Calendar.current.isDateInToday($0) }
        }
    }

    private func countOne() {
        var made = madeToday()
        made.append(Date())
        countLock.withLock { _ in
            try? JSONSettingsStore.encoder.encode(made).write(to: countFile, options: .atomic)
        }
    }
}
