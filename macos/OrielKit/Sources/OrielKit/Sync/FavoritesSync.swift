import Foundation
import Synchronization

/// One favorite as the account keeps it: the photo, and whether it's (still) a favorite. Never deleted, so an
/// un-favorite syncs too.
public struct SyncedFavorite: Sendable, Hashable {
    public var photoId: String
    public var imageUrl: String
    public var pageUrl: String
    public var photographer: String
    public var photographerUrl: String
    public var description: String?
    public var favorite: Bool
    public var updatedAt: Date
}

/// The same favorites on every device: starring or un-starring here writes `users/{uid}/favorites/{photoId}` and rings
/// the other devices; a ring, or opening the app, brings the changes made elsewhere (only those since the last sync).
/// A favorite of a photo this Mac never showed is added to History, where it can be set (it's downloaded then). The
/// first sync on a device uploads the favorites it already had.
public final class FavoritesSync: Sendable {
    private let auth: AuthService
    private let firebase: FirebaseClient
    private let history: HistoryStore
    private let store: SettingsStore
    private let gate = AsyncGate()
    private let quiet = Mutex(false)

    public init(auth: AuthService, firebase: FirebaseClient, history: HistoryStore, store: SettingsStore) {
        self.auth = auth
        self.firebase = firebase
        self.history = history
        self.store = store
    }

    public func start() {
        history.onFavoriteChanged { [weak self] entry in
            guard let self, !self.quiet.withLock({ $0 }) else { return } // from another device: nothing to send back
            Task { await self.push(entry) }
        }
        auth.changed.subscribe { [weak self] in
            guard let self, self.auth.isSignedIn else { return }
            Task { await self.pull() }
        }
        Task { await pull() }
    }

    private func push(_ entry: HistoryEntry, ring: Bool = true) async {
        guard let favorite = Self.toSynced(entry, at: Date()) else { return }
        do {
            guard let tokens = try await auth.tokens() else { return }
            let device = auth.current?.sessionId ?? ""
            try await firebase.setFavorite(tokens, favorite, device: device)
            if ring { try await firebase.ringFavorites(tokens, device: device) }
        } catch {
            Log.favorites.info("Couldn't share the favorite \(entry.photoID): \(error.localizedDescription)")
        }
    }

    /// Brings here the favorites changed on other devices since the last sync.
    public func pull() async {
        guard auth.isSignedIn, gate.tryLock() else { return }
        defer { gate.unlock() }
        do {
            guard let tokens = try await auth.tokens() else { return }
            let since = store.loadState().favoritesSyncedAt
            let changes = try await firebase.favorites(since: since, tokens)
            let local = history.list(includeDeleted: true)
            quiet.withLock { $0 = true }
            for change in changes.sorted(by: { $0.updatedAt < $1.updatedAt }) { apply(change, local) }
            quiet.withLock { $0 = false }
            if since == nil {
                // First sync here: the account gets this device's favorites too.
                let known = Set(changes.map(\.photoId))
                var seen = Set<String>()
                for mine in history.list() where mine.isFavorite && !known.contains(mine.photoID) && seen.insert(mine.photoID).inserted {
                    await push(mine, ring: false)
                }
                try await firebase.ringFavorites(tokens, device: auth.current?.sessionId ?? "")
            }
            // Rounded up to the millisecond the state file keeps, so the next "since" never asks for the newest again.
            let newest = changes.map(\.updatedAt).max().map { Date(timeIntervalSince1970: ($0.timeIntervalSince1970 * 1000).rounded(.up) / 1000) }
            store.updateState { $0.favoritesSyncedAt = newest ?? since ?? Date(timeIntervalSince1970: 0) }
            if !changes.isEmpty { Log.favorites.info("Took \(changes.count) favorite change(s) from your other devices") }
        } catch {
            quiet.withLock { $0 = false }
            Log.favorites.info("Couldn't read the shared favorites: \(error.localizedDescription)")
        }
    }

    private func apply(_ change: SyncedFavorite, _ local: [HistoryEntry]) {
        let mine = local.filter { $0.photoID == change.photoId }
        if !mine.isEmpty {
            for e in mine where e.isFavorite != change.favorite { history.setFavorite(e.id, change.favorite) }
            return
        }
        guard change.favorite else { return }
        // A photo never shown here: into History, filed so it doesn't become the current wallpaper.
        let newest = local.first { !$0.isDeleted }?.appliedAt
        let at = newest.map { change.updatedAt >= $0 ? $0.addingTimeInterval(-1) : change.updatedAt } ?? change.updatedAt
        // An AI wallpaper's link is used as it is; an Unsplash one gets a thumbnail size.
        let ai = PollinationsClient.isMediaLink(change.imageUrl)
        let entry = HistoryEntry(
            provider: PollinationsClient.isAiPhoto(change.photoId) ? PollinationsClient.provider : "unsplash",
            photoID: change.photoId, filePath: "",
            thumbnailURL: ai ? change.imageUrl : change.imageUrl + (change.imageUrl.contains("?") ? "&" : "?") + "w=640&q=80&fm=jpg&fit=max",
            photoPageURL: change.pageUrl, photographerName: change.photographer, photographerURL: change.photographerUrl,
            description: change.description, topic: "favorite", appliedAt: at, isFavorite: true)
        if let id = try? history.add(entry) { history.setFavorite(id, true) }
    }

    /// Unsplash photos and AI wallpapers can be shared between devices (every device fetches its own copy).
    static func toSynced(_ e: HistoryEntry, at: Date) -> SyncedFavorite? {
        if PollinationsClient.isMediaLink(e.thumbnailURL) {
            return SyncedFavorite(photoId: e.photoID, imageUrl: e.thumbnailURL, pageUrl: e.photoPageURL, photographer: e.photographerName,
                                  photographerUrl: e.photographerURL, description: e.description, favorite: e.isFavorite, updatedAt: at)
        }
        guard let image = URL(string: e.thumbnailURL), image.host() == "images.unsplash.com" else { return nil }
        return SyncedFavorite(photoId: e.photoID, imageUrl: "https://images.unsplash.com\(image.path(percentEncoded: true))",
                              pageUrl: e.photoPageURL, photographer: e.photographerName, photographerUrl: e.photographerURL,
                              description: e.description, favorite: e.isFavorite, updatedAt: at)
    }
}
