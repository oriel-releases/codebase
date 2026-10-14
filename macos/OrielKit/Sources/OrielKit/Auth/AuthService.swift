import Foundation
import Synchronization

/// The signed-in account (Google or email and password), as kept on this Mac.
public struct Account: Codable, Sendable, Equatable {
    public var uid = ""
    public var email = ""
    public var displayName = ""
    public var photoUrl = ""
    /// Firebase refresh token: lets the app get fresh ID tokens without asking again.
    public var refreshToken = ""
    /// This install's document in `users/{uid}/sessions`.
    public var sessionId = ""
    public var signedInAt = Date()

    public init() {}

    enum CodingKeys: String, CodingKey {
        case uid = "Uid", email = "Email", displayName = "DisplayName", photoUrl = "PhotoUrl", refreshToken = "RefreshToken",
             sessionId = "SessionId", signedInAt = "SignedInAt"
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        uid = try c.decodeIfPresent(String.self, forKey: .uid) ?? ""
        email = try c.decodeIfPresent(String.self, forKey: .email) ?? ""
        displayName = try c.decodeIfPresent(String.self, forKey: .displayName) ?? ""
        photoUrl = try c.decodeIfPresent(String.self, forKey: .photoUrl) ?? ""
        refreshToken = try c.decodeIfPresent(String.self, forKey: .refreshToken) ?? ""
        sessionId = try c.decodeIfPresent(String.self, forKey: .sessionId) ?? ""
        signedInAt = try c.decodeIfPresent(Date.self, forKey: .signedInAt) ?? Date()
    }
}

public protocol AccountStore: Sendable {
    func load() -> Account?
    func save(_ account: Account)
    func clear()
}

/// Keeps the account in `auth.json`, readable only by this user (mode 0600). Not the Keychain: without a paid
/// Developer ID every update is signed differently, and the Keychain would ask for permission after each one.
public struct FileAccountStore: AccountStore {
    let file: URL

    public init(paths: AppPaths) {
        file = paths.accountFile
    }

    public func load() -> Account? {
        guard let data = try? Data(contentsOf: file),
              let account = try? JSONSettingsStore.decoder.decode(Account.self, from: data),
              !account.refreshToken.isEmpty, !account.uid.isEmpty else { return nil }
        return account
    }

    public func save(_ account: Account) {
        do {
            try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            let tmp = file.appendingPathExtension("tmp")
            let data = try JSONSettingsStore.encoder.encode(account)
            // Created owner-only before the token is written into it.
            FileManager.default.createFile(atPath: tmp.path(percentEncoded: false), contents: nil, attributes: [.posixPermissions: 0o600])
            try data.write(to: tmp)
            _ = try FileManager.default.replaceItemAt(file, withItemAt: tmp)
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: file.path(percentEncoded: false))
        } catch {
            Log.auth.warning("Couldn't save the account", error)
        }
    }

    public func clear() {
        try? FileManager.default.removeItem(at: file)
    }
}

/// The app's sign-in. Signing in happens on the website; it hands over a Google ID token (or, for an account without
/// Google, the Firebase session the website made with the email and password) that this turns into the app's own
/// Firebase session (a refresh token kept on this Mac) plus a session document in `users/{uid}/sessions`. After that
/// the app stays signed in, even offline. It signs out only when Google rejects the account or the session document
/// was deleted (revoked from somewhere else).
public final class AuthService: AuthState, Sendable {
    static let checkInterval: TimeInterval = 6 * 3600

    private let store: AccountStore
    private let firebase: FirebaseClient
    private let appVersion: String
    private let deviceName: String
    private let gate = AsyncGate()
    private let state: Mutex<(account: Account?, tokens: FirebaseTokens?, signingOut: Bool)>
    private let timer = Mutex<Task<Void, Never>?>(nil)

    /// After signing in or out, from whatever task changed it.
    public let changed = Broadcaster<Void>()
    /// When `isSigningOut` changes: the window covers itself until it's done.
    public let signingOutChanged = Broadcaster<Bool>()

    public init(store: AccountStore, firebase: FirebaseClient, appVersion: String, deviceName: String) {
        self.store = store
        self.firebase = firebase
        self.appVersion = appVersion
        self.deviceName = deviceName
        state = Mutex((store.load(), nil, false))
    }

    public var isSignedIn: Bool { state.withLock { $0.account != nil } }
    public var current: Account? { state.withLock { $0.account } }
    public var isSigningOut: Bool { state.withLock { $0.signingOut } }

    /// Checks the account 5 seconds from now and then every few hours, without ever blocking the app.
    public func start() {
        timer.withLock { task in
            guard task == nil else { return }
            task = Task { [weak self] in
                try? await Task.sleep(for: .seconds(5))
                while !Task.isCancelled {
                    await self?.validate()
                    try? await Task.sleep(for: .seconds(Self.checkInterval))
                }
            }
        }
    }

    public func signIn(googleIdToken: String) async throws {
        try await signIn { try await self.firebase.signInWithGoogle(googleIdToken) }
    }

    /// Signs in with the Firebase session the website made for an email and password account.
    public func signIn(refreshToken: String) async throws {
        try await signIn { try await self.firebase.signInWithRefreshToken(refreshToken) }
    }

    private func signIn(_ exchange: @Sendable () async throws -> FirebaseProfile) async throws {
        await gate.lock()
        do {
            let profile = try await exchange()
            var account = Account()
            account.uid = profile.uid
            account.email = profile.email
            account.displayName = profile.displayName
            account.photoUrl = profile.photoURL
            account.refreshToken = profile.tokens.refreshToken
            account.sessionId = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
            account.signedInAt = Date()
            // Record this install first: if that fails, nothing is saved and the person can just try again.
            try await firebase.setSession(profile.tokens, sessionId: account.sessionId,
                                          info: SessionInfo(platform: "macos", deviceName: deviceName, appVersion: appVersion),
                                          createdAt: account.signedInAt)
            store.save(account)
            state.withLock {
                $0.account = account
                $0.tokens = profile.tokens
            }
            gate.unlock()
        } catch {
            gate.unlock()
            throw error
        }
        Log.auth.info("Signed in")
        changed.send(())
    }

    /// Fresh Firebase credentials for the signed-in account, or nil when signed out.
    public func tokens() async throws -> FirebaseTokens? {
        guard let account = current else { return nil }
        await gate.lock()
        defer { gate.unlock() }
        return try await ensureTokens(account)
    }

    /// Signs out here. Removing this session's document takes a moment (at most 8 seconds: on a bad connection,
    /// signing out locally is what matters).
    public func signOut() async {
        guard let account = current, !isSigningOut else { return }
        state.withLock { $0.signingOut = true }
        signingOutChanged.send(true)
        await gate.lock()
        do {
            try await withTimeout(8) { [self] in
                let tokens = try await ensureTokens(account)
                try await firebase.deleteSession(tokens, sessionId: account.sessionId)
            }
        } catch {
            // Offline or already revoked: signing out locally is what matters.
            Log.auth.info("Couldn't remove the session document: \(error.localizedDescription)")
        }
        forget()
        gate.unlock()
        changed.send(())
        state.withLock { $0.signingOut = false }
        signingOutChanged.send(false)
    }

    /// Refreshes the token and records activity. Signs out if the account or this session no longer exists.
    public func validate() async {
        guard let account = current, gate.tryLock() else { return }
        var signedOut = false
        do {
            let tokens = try await ensureTokens(account)
            if try await !firebase.touchSession(tokens, sessionId: account.sessionId, appVersion: appVersion) {
                Log.auth.info("This session was removed; signing out")
                forget()
                signedOut = true
            }
        } catch let error as FirebaseError where error.failure == .rejected {
            Log.auth.info("Google no longer accepts this account (\(error.message)); signing out")
            forget()
            signedOut = true
        } catch {
            // Offline or Google is down: stay signed in and check again later.
            Log.auth.info("Couldn't check the account: \(error.localizedDescription)")
        }
        gate.unlock()
        if signedOut { changed.send(()) }
    }

    private func ensureTokens(_ account: Account) async throws -> FirebaseTokens {
        if let t = state.withLock({ $0.tokens }), t.expiresAt > Date().addingTimeInterval(120) { return t }
        let fresh = try await firebase.refresh(account.refreshToken)
        if fresh.refreshToken != account.refreshToken && !fresh.refreshToken.isEmpty {
            var updated = account
            updated.refreshToken = fresh.refreshToken
            store.save(updated)
            state.withLock { $0.account = updated }
        }
        state.withLock { $0.tokens = fresh }
        return fresh
    }

    private func forget() {
        store.clear()
        state.withLock {
            $0.account = nil
            $0.tokens = nil
        }
    }
}
