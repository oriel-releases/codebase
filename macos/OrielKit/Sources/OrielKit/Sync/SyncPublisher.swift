import Foundation

/// Writes `users/{uid}/sync/current` so the account's other devices (and the browser extension) show the same photo,
/// then rings their doorbell. Only the photo's ID and links are shared, never the image file: an Unsplash photo (each
/// device makes its own crop) or an AI wallpaper's public Pollinations link. Best effort: being offline never affects
/// changing the wallpaper.
public struct SyncPublisher: SyncPublishing {
    static let source = "macos"
    static let timeout: Double = 8

    let auth: AuthService
    let firebase: FirebaseClient
    let history: HistoryStore
    let settings: SettingsStore
    let accent: AccentSampler

    public init(auth: AuthService, firebase: FirebaseClient, history: HistoryStore, settings: SettingsStore, accent: AccentSampler) {
        self.auth = auth
        self.firebase = firebase
        self.history = history
        self.settings = settings
        self.accent = accent
    }

    public func publish(_ entry: HistoryEntry) async {
        await run(entry, onlyIfMissing: false)
    }

    /// Publishes the newest history entry if the account has no shared wallpaper yet, so a freshly connected extension
    /// has something to show. An existing one is left alone: it may come from another device and be newer.
    public func publishLatestIfMissing() async {
        if let latest = history.list().first { await run(latest, onlyIfMissing: true) }
    }

    private func run(_ entry: HistoryEntry, onlyIfMissing: Bool) async {
        guard auth.isSignedIn else {
            Log.sync.info("Not sharing the wallpaper: not signed in")
            return
        }
        guard let document = Self.document(entry) else {
            Log.sync.info("Not sharing the wallpaper: \(entry.photoID) isn't an Unsplash photo or an AI wallpaper")
            return
        }
        do {
            try await withTimeout(Self.timeout) { [self] in
                guard let tokens = try await auth.tokens() else { return }
                if onlyIfMissing, try await firebase.hasCurrentWallpaper(tokens) { return }
                var withColor = document
                // The color chosen for it (on Home, or on another device), else the one picked from it.
                withColor.color = chosenColor(entry) ?? accent.sample(entry.filePath)
                try await firebase.setCurrentWallpaper(tokens, withColor)
                // The doorbell: devices listening hear about it now instead of at their next poll.
                if !onlyIfMissing {
                    do {
                        try await firebase.ring(tokens, photoId: entry.photoID, source: Self.source, device: auth.current?.sessionId ?? "")
                    } catch {
                        Log.sync.info("Couldn't ring the other devices (they still poll): \(error.localizedDescription)")
                    }
                }
                Log.sync.info("Shared the wallpaper \(entry.photoID) with your other devices")
                // Sharing with friends: your follow page shows the new wallpaper too.
                if !onlyIfMissing, let code = settings.load().myFollowCode, let photo = ShareLink.photo(entry) {
                    try await firebase.setFollowCodePhoto(tokens, code, photo)
                }
            }
        } catch {
            // Offline, rejected by the rules, or anything unexpected: the wallpaper has already changed, so only note it.
            Log.sync.warning("Couldn't share the wallpaper \(entry.photoID): \(error.localizedDescription)")
        }
    }

    /// The account's other devices learn of the color chosen for the photo on screen. Only while it is still the
    /// account's photo: another device may have set a newer one meanwhile.
    public func publishColor(_ entry: HistoryEntry, _ color: String) async {
        guard auth.isSignedIn, Self.document(entry) != nil else { return }
        do {
            try await withTimeout(Self.timeout) { [self] in
                guard let tokens = try await auth.tokens(),
                      let current = try await firebase.currentPhoto(tokens, owner: tokens.uid), current.photoId == entry.photoID else { return }
                try await firebase.setCurrentColor(tokens, color)
                try await firebase.ring(tokens, photoId: entry.photoID, source: Self.source, device: auth.current?.sessionId ?? "")
                Log.sync.info("Shared the accent color \(color) of \(entry.photoID) with your other devices")
            }
        } catch {
            Log.sync.warning("Couldn't share the accent color of \(entry.photoID): \(error.localizedDescription)")
        }
    }

    private func chosenColor(_ entry: HistoryEntry) -> String? {
        let state = settings.loadState()
        guard state.accentPhotoId == entry.photoID else { return nil }
        return state.accentColor
    }

    /// Unsplash photos and AI wallpapers (kept by Pollinations at a public link) can be shared: the other devices
    /// hotlink or download them. Anything else is skipped.
    static func document(_ e: HistoryEntry) -> CurrentWallpaper? {
        guard let url = URL(string: e.thumbnailURL),
              url.host() == "images.unsplash.com" || PollinationsClient.isMediaLink(e.thumbnailURL) else { return nil }
        return CurrentWallpaper(photoId: e.photoID, imageUrl: e.thumbnailURL, pageUrl: e.photoPageURL, photographer: e.photographerName,
                                photographerUrl: e.photographerURL, description: e.description, topic: e.topic, source: source)
    }
}
