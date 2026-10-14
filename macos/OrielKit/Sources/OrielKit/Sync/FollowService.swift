import Foundation
import Synchronization

/// A friendly reason why following or sharing didn't work, ready to show.
public struct FollowError: Error, LocalizedError, Sendable {
    public var message: String
    public init(_ message: String) { self.message = message }
    public var errorDescription: String? { message }
}

/// Follow a friend: your desktop shows whatever wallpaper they set. The friend shares a short code; following it
/// writes you into their `followers`, which is what lets you read their `sync/current`. While you follow, your own
/// schedule is off. Also shows the wallpaper the account's other devices set (`check()`).
public final class FollowService: Sendable {
    private let auth: AuthService
    private let firebase: FirebaseClient
    private let store: SettingsStore
    private let wallpapers: WallpaperService
    private let history: HistoryStore
    private let checking = AsyncGate()
    private let link = Mutex<(code: String?, error: String?)>((nil, nil))

    /// After following, sharing or the friend's photo changed, so the UI can refresh.
    public let changed = Broadcaster<Void>()

    public init(auth: AuthService, firebase: FirebaseClient, store: SettingsStore, wallpapers: WallpaperService, history: HistoryStore) {
        self.auth = auth
        self.firebase = firebase
        self.store = store
        self.wallpapers = wallpapers
        self.history = history
    }

    private var myName: String {
        let n = ShareLink.cleanName(auth.current?.displayName)
        return n.isEmpty ? "A friend" : n
    }

    private func tokens() async throws -> FirebaseTokens {
        do {
            guard let t = try await auth.tokens() else { throw FollowError("Sign in to Oriel first.") }
            return t
        } catch {
            throw Self.friendly(error, "Sign in to Oriel first.")
        }
    }

    private static func friendly(_ error: Error, _ otherwise: String) -> FollowError {
        if let e = error as? FollowError { return e }
        return FollowError((error as? FirebaseError)?.failure == .network ? "You seem to be offline. Try again." : otherwise)
    }

    // MARK: - Sharing your wallpaper

    /// Your follow code, made the first time you share and the same one after that.
    public func ensureCode() async throws -> String {
        if let code = store.load().myFollowCode { return code }
        return try await startSharing()
    }

    /// Starts sharing: a new code friends can follow. Returns it.
    public func startSharing() async throws -> String {
        let tokens = try await tokens()
        var attempt = 0
        while true {
            let code = FollowCodes.new()
            do {
                try await firebase.createFollowCode(tokens, code, name: myName, picture: ShareLink.picture(auth.current?.photoUrl))
                update { $0.myFollowCode = code }
                // The follow page shows the wallpaper you have now; after this it follows every change (SyncPublisher).
                if let current = history.list().first { await showOnFollowPage(tokens, code, current) }
                return code
            } catch let error as FirebaseError where error.failure != .network && attempt < 3 {
                Log.follow.info("Follow code taken or refused, trying another: \(error.message)")
                attempt += 1
            } catch {
                throw Self.friendly(error, "Couldn't create a code. Try again.")
            }
        }
    }

    /// Puts this wallpaper on your follow page. Best effort.
    func showOnFollowPage(_ tokens: FirebaseTokens, _ code: String, _ entry: HistoryEntry) async {
        guard let photo = ShareLink.photo(entry) else { return }
        do {
            try await firebase.setFollowCodePhoto(tokens, code, photo)
        } catch {
            Log.follow.info("Couldn't put the wallpaper on the follow page: \(error.localizedDescription)")
        }
    }

    /// Stops sharing: the code stops working and everyone following stops seeing your wallpaper.
    public func stopSharing() async throws {
        let tokens = try await tokens()
        do {
            if let code = store.load().myFollowCode { try await firebase.deleteFollowCode(tokens, code) }
            for follower in try await firebase.followers(tokens) {
                try await firebase.removeFollower(tokens, owner: tokens.uid, follower: follower.uid)
            }
        } catch {
            throw Self.friendly(error, "Couldn't stop sharing. Try again.")
        }
        update { $0.myFollowCode = nil }
    }

    /// The people following you, newest first; nil when it can't be read right now.
    public func listFollowers() async -> [Follower]? {
        guard let tokens = try? await tokens() else { return nil }
        return try? await firebase.followers(tokens)
    }

    /// Stops one person seeing your wallpaper.
    public func removeFollower(_ uid: String) async throws {
        let tokens = try await tokens()
        do {
            try await firebase.removeFollower(tokens, owner: tokens.uid, follower: uid)
        } catch {
            throw Self.friendly(error, "Couldn't remove them. Try again.")
        }
    }

    // MARK: - Following a friend

    /// Follows the friend whose code this is. Returns their first name.
    @discardableResult
    public func follow(_ input: String) async throws -> String {
        guard let code = FollowCodes.normalize(input) else { throw FollowError("A code has 8 letters and numbers, like K7MQ-2XPA.") }
        let tokens = try await tokens()
        do {
            guard let owner = try await firebase.followCode(tokens, code) else {
                throw FollowError("No one has that code. Check it and try again.")
            }
            if owner.uid == tokens.uid { throw FollowError("That's your own code. Send it to a friend instead.") }
            if let previous = store.load().followUid, previous != owner.uid {
                do {
                    try await firebase.removeFollower(tokens, owner: previous, follower: tokens.uid)
                } catch {
                    Log.follow.info("Couldn't leave the previous friend: \(error.localizedDescription)")
                }
            }
            try await firebase.addFollower(tokens, friendUid: owner.uid, code: code, name: myName)
            let name = owner.name.isEmpty ? "your friend" : owner.name
            update {
                $0.followUid = owner.uid
                $0.followName = name
                $0.followPicture = owner.picture.isEmpty ? nil : owner.picture
            }
            store.updateState {
                $0.followedPhotoId = nil
                $0.followProblem = nil
            }
            await check()
            return name
        } catch {
            throw Self.friendly(error, "Couldn't follow with that code. Try again.")
        }
    }

    /// A follow link that is being followed now (or waits for sign-in), for the page to show.
    public var linkCode: String? { link.withLock { $0.code } }
    /// Why the last follow link didn't work, for the page to show once.
    public var linkError: String? { link.withLock { $0.error } }

    /// Someone clicked "Follow" on a friend's follow page. Follows now if signed in; otherwise after sign-in.
    public func followFromLink(_ code: String) async {
        link.withLock { $0 = (code, nil) }
        changed.send(())
        if auth.isSignedIn { await followWaitingLink() }
    }

    /// Follows the link that was waiting for sign-in, if there is one.
    public func followWaitingLink() async {
        guard let code = linkCode, auth.isSignedIn else { return }
        var failure: String?
        do {
            try await follow(code)
        } catch {
            failure = error.localizedDescription
        }
        link.withLock { $0 = (nil, failure) }
        changed.send(())
    }

    /// The page showed the link's error: forget it.
    public func clearLinkError() {
        link.withLock { $0.error = nil }
    }

    /// Stops following. Your own schedule takes over again.
    public func unfollow() async {
        if let friend = store.load().followUid {
            do {
                if let tokens = try await auth.tokens() { try await firebase.removeFollower(tokens, owner: friend, follower: tokens.uid) }
            } catch {
                // The friend may have stopped sharing already; leaving locally is what matters.
                Log.follow.info("Couldn't remove the follow: \(error.localizedDescription)")
            }
        }
        update {
            $0.followUid = nil
            $0.followName = nil
            $0.followPicture = nil
        }
        store.updateState {
            $0.followedPhotoId = nil
            $0.followProblem = nil
        }
    }

    /// Shows the wallpaper the account's other devices set last (their `sync/current`), or the followed friend's, when
    /// it's new here. Called by the scheduler every few minutes, before a scheduled change, and on each doorbell ring.
    /// Locked (paused) only stops the schedule: a photo set by hand on another device still shows here.
    public func check() async {
        let settings = store.load()
        let following = settings.isFollowing
        guard checking.tryLock() else { return }
        defer { checking.unlock() }
        do {
            guard let tokens = try await auth.tokens() else { return }
            let owner = settings.followUid ?? tokens.uid
            let photoId: String?
            do {
                photoId = try await firebase.currentPhotoId(tokens, owner: owner)
            } catch let error as FirebaseError where error.failure == .rejected && following {
                setProblem("\(settings.followName ?? "Your friend") stopped sharing their wallpaper.")
                return
            }
            var state = store.loadState()
            guard let photoId, photoId != state.followedPhotoId else {
                if state.followProblem != nil { setProblem(nil) }
                return
            }
            // Already on this device (it published it, or showed it before): nothing to do.
            if history.list().first?.photoID == photoId {
                state.followedPhotoId = photoId
                store.saveState(state)
                return
            }
            wallpapers.reportPhase(.receiving)
            let photo: PhotoCandidate
            do {
                photo = try await wallpapers.sharedPhoto(photoId)
            } catch {
                wallpapers.finishPhase()
                throw error
            }
            // A friend's photo is passed on to this account's other devices; the account's own isn't sent back.
            let result = await wallpapers.applyPhoto(photo, topic: following ? "from \(settings.followName ?? "your friend")" : "synced",
                                                     publish: following)
            guard result.status == .applied else { return }
            store.updateState {
                $0.followedPhotoId = photoId
                $0.followProblem = nil
            }
            Log.follow.info(following ? "Applied the followed friend's wallpaper \(photoId)" : "Applied the wallpaper set on another device \(photoId)")
            changed.send(())
        } catch {
            Log.follow.info("Couldn't check the followed wallpaper: \(error.localizedDescription)")
        }
    }

    private func setProblem(_ problem: String?) {
        let state = store.loadState()
        guard state.followProblem != problem else { return }
        store.updateState { $0.followProblem = problem }
        changed.send(())
    }

    private func update(_ change: (inout OrielSettings) -> Void) {
        store.update(change)
        changed.send(())
    }
}

/// Share links for wallpapers: each is a small `shares/{id}` document (the photo, and who sent it) and a short link to
/// it, `oriel.akbarjon.uz/share/AB3K9QXZ`. One per photo for as long as the app runs.
public final class ShareService: Sendable {
    private let auth: AuthService
    private let firebase: FirebaseClient
    private let links = Mutex<[String: String]>([:])
    private let gate = AsyncGate()

    public init(auth: AuthService, firebase: FirebaseClient) {
        self.auth = auth
        self.firebase = firebase
    }

    /// True for photos that can be shared (Unsplash photos).
    public static func canShare(_ entry: HistoryEntry) -> Bool { ShareLink.photo(entry) != nil }

    /// The link for this photo, made the first time it's shared.
    public func link(_ entry: HistoryEntry) async throws -> String {
        guard let photo = ShareLink.photo(entry) else { throw FollowError("Only photos from Unsplash can be shared.") }
        await gate.lock()
        defer { gate.unlock() }
        if let known = links.withLock({ $0[photo.id] }) { return known }
        let tokens: FirebaseTokens
        do {
            guard let t = try await auth.tokens() else { throw FollowError("Sign in to Oriel first.") }
            tokens = t
        } catch let error as FollowError {
            throw error
        } catch {
            throw FollowError((error as? FirebaseError)?.failure == .network ? "You seem to be offline. Try again." : "Couldn't make a link. Try again.")
        }
        let from = ShareLink.cleanName(auth.current?.displayName)
        let picture = ShareLink.picture(auth.current?.photoUrl)
        var attempt = 0
        while true {
            let id = FollowCodes.new()
            do {
                try await firebase.createShare(tokens, id: id, from: from, picture: picture, photo)
                let url = ShareLink.forShare(id)
                links.withLock { $0[photo.id] = url }
                return url
            } catch let error as FirebaseError where error.failure != .network && attempt < 3 {
                Log.share.info("Share id taken or refused, trying another: \(error.message)")
                attempt += 1
            } catch {
                throw FollowError((error as? FirebaseError)?.failure == .network ? "You seem to be offline. Try again." : "Couldn't make a link. Try again.")
            }
        }
    }
}
