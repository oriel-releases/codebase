import Foundation
import Synchronization

/// Hears, within a second, that another device of the account set a new wallpaper (or changed settings or favorites).
/// Every device writes a tiny "doorbell" (`pulse/{uid}` in the Realtime Database) right after publishing; this keeps
/// one Server-Sent Events connection to it open. Plain HTTP, no SDK. When the connection can't be kept, the
/// scheduler's poll every few minutes still brings the photo, just later.
public final class PulseListener: Sendable {
    private let auth: AuthService
    private let follow: FollowService
    private let settings: SettingsSync
    private let favorites: FavoritesSync
    private let session: URLSession
    private let state = Mutex<(task: Task<Void, Never>?, connection: Task<Void, Error>?, connected: Bool)>((nil, nil, false))

    public init(auth: AuthService, follow: FollowService, settings: SettingsSync, favorites: FavoritesSync) {
        self.auth = auth
        self.follow = follow
        self.settings = settings
        self.favorites = favorites
        let config = URLSessionConfiguration.default
        config.timeoutIntervalForRequest = .infinity
        config.timeoutIntervalForResource = .infinity
        config.urlCache = nil
        session = URLSession(configuration: config)
    }

    /// True while the live connection is open; the scheduler then polls only rarely.
    public var isConnected: Bool { state.withLock { $0.connected } }

    public func start() {
        // Signing in or out: drop the connection, so the next one is for the right account (or none).
        auth.changed.subscribe { [weak self] in self?.state.withLock { $0.connection?.cancel() } }
        state.withLock { s in
            guard s.task == nil else { return }
            s.task = Task { [weak self] in await self?.run() }
        }
    }

    private func run() async {
        var delay: Double = 2
        while !Task.isCancelled {
            let connection = Task { [self] in
                guard auth.isSignedIn, let tokens = try await auth.tokens() else {
                    try await Task.sleep(for: .seconds(30))
                    throw CancellationError()
                }
                try await listen(tokens)
            }
            state.withLock { $0.connection = connection }
            do {
                try await connection.value
                delay = 2 // ended normally (the hourly token expired): reconnect right away
                setConnected(false)
                continue
            } catch is CancellationError {
                // Not signed in yet, or the account changed: start over.
                setConnected(false)
                continue
            } catch {
                if (error as? URLError)?.code == .cancelled {
                    setConnected(false)
                    continue
                }
                Log.pulse.info("Live sync connection: \(error.localizedDescription)")
            }
            setConnected(false)
            try? await Task.sleep(for: .seconds(delay))
            delay = min(delay * 2, 120)
        }
    }

    private func setConnected(_ on: Bool) {
        state.withLock { $0.connected = on }
    }

    private func listen(_ tokens: FirebaseTokens) async throws {
        var request = URLRequest(url: FirebaseClient.pulseURL(tokens))
        request.setValue("text/event-stream", forHTTPHeaderField: "Accept")
        let (bytes, response) = try await session.bytes(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw URLError(.badServerResponse)
        }
        setConnected(true)
        Log.pulse.info("Listening for wallpapers from your other devices")

        var name: String?
        for try await line in bytes.lines {
            if line.hasPrefix("event:") {
                name = line.dropFirst(6).trimmingCharacters(in: .whitespaces)
            } else if line.hasPrefix("data:") {
                switch name {
                case "put", "patch":
                    onPulse(String(line.dropFirst(5)).trimmingCharacters(in: .whitespaces), initial: name == "put")
                case "auth_revoked", "cancel":
                    return // the ID token expired (hourly) or access ended: reconnect with a fresh one
                default:
                    break
                }
            }
        }
    }

    /// The first event ("put") is the doorbell as it is now, catching up after a reconnect: everything is checked.
    /// Later rings are PATCHes carrying photo, settings or favorites fields; this device's own rings are skipped.
    private func onPulse(_ json: String, initial: Bool) {
        guard let object = try? JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any],
              let data = object["data"] as? [String: Any] else { return }
        let me = auth.current?.sessionId
        func field(_ name: String) -> String? { data[name] as? String }
        let photo = data["photoId"] != nil && (initial || field("device") != me)
        let prefs = data["settingsAt"] != nil && (initial || field("settingsDevice") != me)
        let stars = data["favoritesAt"] != nil && (initial || field("favoritesDevice") != me)

        if stars { Task { await favorites.pull() } }
        // Settings first: a new schedule decides whether the photo check should change anything.
        if prefs {
            Task {
                await settings.pull()
                if photo { await follow.check() }
            }
        } else if photo {
            Task { await follow.check() }
        }
    }
}
