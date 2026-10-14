import Foundation

/// Writes `users/{uid}/sync/current` so the account's other devices (and the browser extension) show the same photo,
/// then rings their doorbell. Only the Unsplash photo (ID and links) is shared, never the image file: each device
/// makes its own crop. Best effort: being offline never affects changing the wallpaper.
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
            Log.sync.info("Not sharing the wallpaper: \(entry.photoID) isn't an Unsplash photo")
            return
        }
        do {
            try await withTimeout(Self.timeout) { [self] in
                guard let tokens = try await auth.tokens() else { return }
                if onlyIfMissing, try await firebase.hasCurrentWallpaper(tokens) { return }
                var withColor = document
                withColor.color = accent.sample(entry.filePath)
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

    /// Only Unsplash photos can be shared (the extension hotlinks them); anything else is skipped.
    static func document(_ e: HistoryEntry) -> CurrentWallpaper? {
        guard let url = URL(string: e.thumbnailURL), url.host() == "images.unsplash.com" else { return nil }
        return CurrentWallpaper(photoId: e.photoID, imageUrl: e.thumbnailURL, pageUrl: e.photoPageURL, photographer: e.photographerName,
                                photographerUrl: e.photographerURL, description: e.description, topic: e.topic, source: source)
    }
}
