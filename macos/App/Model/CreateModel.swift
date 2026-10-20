import AppKit
import Observation
import OrielKit

/// A look for an AI wallpaper: words added after the user's own.
struct CreateStyle: Hashable {
    var label: String
    var words: String?
}

/// Ideas and looks for the AI page. A few ideas at random each time, like Search.
enum CreateIdeas {
    static let styles = [
        CreateStyle(label: "Any", words: nil),
        CreateStyle(label: "Photo", words: "photorealistic, natural light, sharp detail"),
        CreateStyle(label: "Painting", words: "oil painting, visible brush strokes, rich colors"),
        CreateStyle(label: "Watercolor", words: "soft watercolor painting, flowing pigments"),
        CreateStyle(label: "Anime", words: "anime background art, soft light, detailed scenery"),
        CreateStyle(label: "Minimal", words: "minimalist, flat colors, simple shapes, lots of empty space"),
        CreateStyle(label: "Abstract", words: "abstract art, flowing shapes, smooth gradients"),
        CreateStyle(label: "3D", words: "3D render, soft studio lighting, clay look"),
        CreateStyle(label: "Pixel art", words: "pixel art, retro game scene"),
    ]

    static let all = [
        "a quiet lake at dawn with mist and mountains", "a cozy cabin in a snowy forest at night",
        "a lighthouse on a cliff during a storm", "a field of lavender under a pink sky",
        "a tiny island with one palm tree in a turquoise sea", "a train crossing a bridge through autumn mountains",
        "a whale swimming through clouds", "a city of glass towers floating above the sea",
        "a desert with giant moons in the sky", "a Japanese street with lanterns in the rain",
        "a library inside a giant tree", "a greenhouse full of plants and warm light",
        "the northern lights over a frozen lake", "a sunflower field at golden hour",
        "a calm ocean seen from a sailboat at sunset", "a waterfall in a jungle full of birds",
        "a fox sleeping in a mossy forest", "an astronaut fishing on the moon",
        "a castle on a mountain above the clouds", "a neon city at night after rain",
        "a hot air balloon festival over green hills", "a koi pond with lily pads from above",
        "a road trip through red rock canyons", "a cat looking at the stars from a rooftop",
        "an underwater city with glowing jellyfish", "cherry blossoms along a river in spring",
        "a mountain village with snow and warm windows", "planets rising over an alien ocean",
        "a paper boat on a puddle reflecting the sky", "a lone tree on a hill under the Milky Way",
        "a cozy reading nook by a rainy window", "waves of colorful sand dunes",
    ]

    static func pick(_ count: Int = 6) -> [String] { Array(all.shuffled().prefix(count)) }
}

/// The AI page: a few words and a look become a full wallpaper, shown first and set when asked.
@MainActor
@Observable
final class CreateModel {
    private let sp: Services
    weak var app: AppModel?

    var text = "" {
        didSet {
            if text.count > 400 { text = String(text.prefix(400)) }
            // New words: the picture's button starts over.
            if text != oldValue { setDone = false }
        }
    }
    var style = CreateIdeas.styles[0]
    private(set) var ideas = CreateIdeas.pick()
    private(set) var isCreating = false
    /// "Imagining…" then "Painting…" while creating.
    private(set) var step = "Imagining…"
    private(set) var photo: PhotoCandidate?
    private(set) var imageData: Data?
    /// Shown a moment after the picture area, so its reveal plays on screen.
    private(set) var preview: NSImage?
    private(set) var setWords: String?
    private(set) var setDone = false
    var banner: Banner?
    /// Moves the cursor to the end of the field (after an idea fills it in).
    var focusField = 0

    init(services: Services) {
        sp = services
    }

    var isConfigured: Bool { sp.ai.isConfigured }
    var leftToday: Int { sp.ai.leftToday }
    var hasPicture: Bool { photo != nil }

    func shuffle() { ideas = CreateIdeas.pick() }

    /// An idea only fills in the words (it doesn't create).
    func use(_ idea: String) {
        text = idea.capitalizedFirst
        focusField += 1
    }

    /// A random idea and look, made right away.
    func lucky() async {
        guard !isCreating else { return }
        text = CreateIdeas.all.randomElement()!.capitalizedFirst
        style = CreateIdeas.styles.randomElement()!
        await create()
    }

    func create() async {
        let words = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !words.isEmpty, !isCreating else { return }
        // The previous picture goes at once and never shows again before the new one.
        photo = nil
        preview = nil
        imageData = nil
        setDone = false
        banner = nil
        isCreating = true
        step = "Imagining…"
        defer { isCreating = false }
        do {
            let ai = sp.ai
            let screen = await sp.wallpapers.screenSize()
            let (made, image) = try await ai.create(prompt: words, style: style.words, seed: PollinationsClient.newSeed(), screen: screen) { [weak self] words in
                Task { @MainActor in self?.step = words }
            }
            let decoded = await Task.detached(priority: .userInitiated) { ImageCache.decode(image, 2400) }.value
            photo = made
            imageData = image
            // The picture area first, the image a frame later, so the reveal plays.
            try? await Task.sleep(for: .milliseconds(30))
            preview = decoded.map { NSImage(cgImage: $0, size: NSSize(width: $0.width, height: $0.height)) }
        } catch {
            let kind = (error as? ProviderError)?.kind
            banner = Banner(severity: kind == .rateLimited || kind == .network ? .warning : .error,
                            title: "Couldn't create it", message: error.localizedDescription)
        }
    }

    func set() async {
        guard let photo, let imageData, setWords == nil, let app else { return }
        setWords = ApplyPhase.changing.text
        banner = nil
        let wallpapers = sp.wallpapers
        let result = await app.watchPhases({ await wallpapers.applyCreated(photo, image: imageData) },
                                           report: { [weak self] words in self?.setWords = words })
        setWords = nil
        if result.status == .applied {
            setDone = true
        } else {
            banner = Banner(severity: .warning, title: "Couldn't set the wallpaper", message: result.error ?? "Try again.")
        }
        app.reload()
    }
}
