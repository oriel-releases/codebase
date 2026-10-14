import Foundation

/// Why a Firebase call failed. `rejected` means Google answered and said no (bad or revoked credentials, rules).
public enum FirebaseFailure: Sendable, Equatable {
    case network, rejected, notFound, other
}

public struct FirebaseError: Error, LocalizedError, Sendable {
    public var failure: FirebaseFailure
    public var message: String

    public init(_ failure: FirebaseFailure, _ message: String) {
        self.failure = failure
        self.message = message
    }

    public var errorDescription: String? { message }
}

public struct FirebaseTokens: Sendable, Equatable {
    public var idToken: String
    public var refreshToken: String
    public var expiresAt: Date
    public var uid: String

    public init(idToken: String, refreshToken: String, expiresAt: Date, uid: String) {
        self.idToken = idToken
        self.refreshToken = refreshToken
        self.expiresAt = expiresAt
        self.uid = uid
    }
}

public struct FirebaseProfile: Sendable {
    public var uid: String
    public var email: String
    public var displayName: String
    public var photoURL: String
    public var tokens: FirebaseTokens
}

/// Someone following this account: their account, first name and since when.
public struct Follower: Sendable, Hashable, Identifiable {
    public var uid: String
    public var name: String
    public var since: Date?

    public var id: String { uid }
}

public struct SessionInfo: Sendable {
    public var platform: String
    public var deviceName: String
    public var appVersion: String

    public init(platform: String, deviceName: String, appVersion: String) {
        self.platform = platform
        self.deviceName = deviceName
        self.appVersion = appVersion
    }
}

/// The wallpaper a device applied last, shared through `users/{uid}/sync/current`.
public struct CurrentWallpaper: Sendable {
    public var photoId: String
    public var imageUrl: String
    public var pageUrl: String
    public var photographer: String
    public var photographerUrl: String
    public var description: String?
    public var topic: String?
    public var source: String
    public var color: String?
}

/// Firebase Auth, Firestore and the Realtime Database over their REST APIs (there's no Firebase SDK in Oriel). The key
/// is the public web key: what protects the data is Firebase Auth plus the security rules, not secrecy of this value.
public struct FirebaseClient: Sendable {
    static let apiKey = "AIzaSyCtUNKnXo3UcKHsG_gtupyf1Tfpo-BzKc8"
    static let documentsPath = "projects/oriel-gcp/databases/(default)/documents"
    static let documentsURL = "https://firestore.googleapis.com/v1/" + documentsPath
    /// The Realtime Database: only the doorbell `pulse/{uid}` lives there.
    static let realtimeURL = "https://oriel-gcp-default-rtdb.firebaseio.com"

    let http: HTTPClient

    public init(http: HTTPClient = URLSession.oriel) {
        self.http = http
    }

    /// The account's doorbell, readable and writable with its ID token (also as a live event stream).
    public static func pulseURL(_ tokens: FirebaseTokens) -> URL {
        var c = URLComponents(string: "\(realtimeURL)/pulse/\(tokens.uid).json")!
        c.queryItems = [URLQueryItem(name: "auth", value: tokens.idToken)]
        return c.url!
    }

    // MARK: - Auth

    /// Exchanges the Google ID token from the website for this app's own Firebase session.
    public func signInWithGoogle(_ googleIdToken: String) async throws -> FirebaseProfile {
        let body: [String: Any] = [
            "postBody": "id_token=\(Self.escape(googleIdToken))&providerId=google.com",
            "requestUri": "http://localhost",
            "returnSecureToken": true,
            "returnIdpCredential": true,
        ]
        let r = try await sendJSON("POST", "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=\(Self.apiKey)", body)
        let uid = r["localId"] as? String ?? ""
        return FirebaseProfile(uid: uid, email: r["email"] as? String ?? "", displayName: r["displayName"] as? String ?? "",
                               photoURL: r["photoUrl"] as? String ?? "",
                               tokens: Self.tokens(r["idToken"] as? String, r["refreshToken"] as? String, r["expiresIn"], uid))
    }

    /// Takes over the Firebase session the website made for an email and password account: fresh tokens from its
    /// refresh token, then the profile, which the token exchange doesn't return.
    public func signInWithRefreshToken(_ refreshToken: String) async throws -> FirebaseProfile {
        let tokens = try await refresh(refreshToken)
        let r = try await sendJSON("POST", "https://identitytoolkit.googleapis.com/v1/accounts:lookup?key=\(Self.apiKey)",
                                   ["idToken": tokens.idToken])
        guard let user = (r["users"] as? [[String: Any]])?.first else { throw FirebaseError(.rejected, "Account not found.") }
        return FirebaseProfile(uid: tokens.uid, email: user["email"] as? String ?? "", displayName: user["displayName"] as? String ?? "",
                               photoURL: user["photoUrl"] as? String ?? "", tokens: tokens)
    }

    public func refresh(_ refreshToken: String) async throws -> FirebaseTokens {
        var request = URLRequest(url: URL(string: "https://securetoken.googleapis.com/v1/token?key=\(Self.apiKey)")!)
        request.httpMethod = "POST"
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        request.httpBody = Data("grant_type=refresh_token&refresh_token=\(Self.escape(refreshToken))".utf8)
        let r = try await object(try await send(request))
        return Self.tokens(r["id_token"] as? String, r["refresh_token"] as? String, r["expires_in"], r["user_id"] as? String ?? "")
    }

    // MARK: - Sessions

    /// Creates or replaces this install's session document.
    public func setSession(_ tokens: FirebaseTokens, sessionId: String, info: SessionInfo, createdAt: Date) async throws {
        try await writeDocument(sessionURL(tokens.uid, sessionId), tokens, [
            "platform": Self.str(info.platform),
            "deviceName": Self.str(info.deviceName),
            "appVersion": Self.str(info.appVersion),
            "createdAt": Self.time(createdAt),
            "lastSeenAt": Self.time(Date()),
        ])
    }

    /// Bumps lastSeenAt and appVersion. False if the session document is gone (signed out elsewhere).
    public func touchSession(_ tokens: FirebaseTokens, sessionId: String, appVersion: String) async throws -> Bool {
        let url = sessionURL(tokens.uid, sessionId)
            + "?updateMask.fieldPaths=lastSeenAt&updateMask.fieldPaths=appVersion&currentDocument.exists=true"
        do {
            try await writeDocument(url, tokens, ["lastSeenAt": Self.time(Date()), "appVersion": Self.str(appVersion)])
            return true
        } catch let error as FirebaseError where error.failure == .notFound {
            return false
        }
    }

    public func deleteSession(_ tokens: FirebaseTokens, sessionId: String) async throws {
        try await deleteDocument(sessionURL(tokens.uid, sessionId), tokens)
    }

    // MARK: - The current wallpaper

    /// Publishes the wallpaper just applied (creates or replaces `sync/current`; the latest write wins).
    public func setCurrentWallpaper(_ tokens: FirebaseTokens, _ w: CurrentWallpaper) async throws {
        var fields: [String: Any] = [
            "photoId": Self.str(w.photoId),
            "imageUrl": Self.str(w.imageUrl),
            "pageUrl": Self.str(w.pageUrl),
            "photographer": Self.str(w.photographer),
            "photographerUrl": Self.str(w.photographerUrl),
            "source": Self.str(w.source),
            "updatedAt": Self.time(Date()),
        ]
        if let d = w.description, !d.trimmingCharacters(in: .whitespaces).isEmpty { fields["description"] = Self.str(d) }
        if let t = w.topic, !t.trimmingCharacters(in: .whitespaces).isEmpty { fields["topic"] = Self.str(t) }
        // The photo's accent color ("#RRGGBB"): other devices use it as their accent.
        if let c = w.color, !c.trimmingCharacters(in: .whitespaces).isEmpty { fields["color"] = Self.str(c) }
        try await writeDocument(currentURL(tokens.uid), tokens, fields)
    }

    public func hasCurrentWallpaper(_ tokens: FirebaseTokens) async throws -> Bool {
        try await getDocument(currentURL(tokens.uid) + "?mask.fieldPaths=photoId", tokens) != nil
    }

    /// The photo an account has on its desktop now, or nil if none yet. Rejected once a followed friend stops sharing.
    public func currentPhotoId(_ tokens: FirebaseTokens, owner: String) async throws -> String? {
        guard let f = try await getDocument(currentURL(owner) + "?mask.fieldPaths=photoId", tokens) else { return nil }
        return Self.stringField(f, "photoId")
    }

    /// Rings the account's doorbell after `sync/current` changed. `device` lets this device ignore its own ring.
    public func ring(_ tokens: FirebaseTokens, photoId: String, source: String, device: String) async throws {
        try await patchPulse(tokens, ["photoId": photoId, "source": source, "device": device, "at": [".sv": "timestamp"]])
    }

    public func ringSettings(_ tokens: FirebaseTokens, device: String) async throws {
        try await patchPulse(tokens, ["settingsAt": [".sv": "timestamp"], "settingsDevice": device])
    }

    public func ringFavorites(_ tokens: FirebaseTokens, device: String) async throws {
        try await patchPulse(tokens, ["favoritesAt": [".sv": "timestamp"], "favoritesDevice": device])
    }

    /// PATCH, never PUT: a photo ring leaves the settings ring's fields alone, and the other way round.
    private func patchPulse(_ tokens: FirebaseTokens, _ body: [String: Any]) async throws {
        var request = URLRequest(url: Self.pulseURL(tokens))
        request.httpMethod = "PATCH"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        _ = try await send(request)
    }

    // MARK: - Stats

    /// Adds 1 to each of `fields` in `stats/{day}` (created on first use). Counts only: no user IDs.
    public func incrementStats(_ tokens: FirebaseTokens, day: String, fields: [String]) async throws {
        let body: [String: Any] = ["writes": [[
            "update": ["name": "\(Self.documentsPath)/stats/\(day)", "fields": [String: Any]()],
            "updateMask": ["fieldPaths": [String]()],
            "updateTransforms": fields.map { ["fieldPath": $0, "increment": ["integerValue": "1"]] },
        ]]]
        _ = try await sendJSON("POST", "\(Self.documentsURL):commit", body, tokens: tokens)
    }

    // MARK: - Shared settings and the schedule's slot

    private func settingsURL(_ uid: String) -> String { "\(Self.documentsURL)/users/\(uid)/sync/settings" }
    private func slotURL(_ uid: String) -> String { "\(Self.documentsURL)/users/\(uid)/sync/slot" }

    /// The account's shared settings and when they were last written, or nil when no device has shared any yet.
    public func syncedSettings(_ tokens: FirebaseTokens) async throws -> (settings: SyncedSettings, updatedAt: Date)? {
        guard let f = try await getDocument(settingsURL(tokens.uid), tokens) else { return nil }
        func int(_ name: String, _ fallback: Int) -> Int {
            ((f[name] as? [String: Any])?["integerValue"] as? String).flatMap(Int.init) ?? fallback
        }
        func bool(_ name: String, in m: [String: Any] = f) -> Bool {
            (m[name] as? [String: Any])?["booleanValue"] as? Bool ?? false
        }
        var interests: [SyncedInterest] = []
        let values = ((f["interests"] as? [String: Any])?["arrayValue"] as? [String: Any])?["values"] as? [[String: Any]] ?? []
        for item in values {
            guard let m = (item["mapValue"] as? [String: Any])?["fields"] as? [String: Any],
                  let query = Self.stringField(m, "query"), !query.isEmpty else { continue }
            interests.append(SyncedInterest(query: query, enabled: bool("enabled", in: m), isCustom: bool("isCustom", in: m)))
        }
        let settings = SyncedSettings(
            dailyTime: Self.stringField(f, "dailyTime") ?? "09:00", timeZone: Self.stringField(f, "timeZone") ?? "UTC",
            intervalMinutes: int("intervalMinutes", 0), paused: bool("paused"), favoritesOnly: bool("favoritesOnly"),
            skipLowLikes: bool("skipLowLikes"), minLikes: int("minLikes", 50), interests: interests,
            matchTimeOfDay: f["matchTimeOfDay"] == nil || bool("matchTimeOfDay"))
        return (settings, Self.timeField(f, "updatedAt") ?? .distantPast)
    }

    public func setSyncedSettings(_ tokens: FirebaseTokens, _ s: SyncedSettings, device: String, at: Date) async throws {
        try await writeDocument(settingsURL(tokens.uid), tokens, [
            "dailyTime": Self.str(s.dailyTime),
            "timeZone": Self.str(s.timeZone),
            "intervalMinutes": ["integerValue": String(s.intervalMinutes)],
            "paused": ["booleanValue": s.paused],
            "favoritesOnly": ["booleanValue": s.favoritesOnly],
            "skipLowLikes": ["booleanValue": s.skipLowLikes],
            "minLikes": ["integerValue": String(s.minLikes)],
            "matchTimeOfDay": ["booleanValue": s.matchTimeOfDay],
            "interests": ["arrayValue": ["values": s.interests.map { i -> [String: Any] in
                ["mapValue": ["fields": [
                    "query": Self.str(i.query),
                    "enabled": ["booleanValue": i.enabled],
                    "isCustom": ["booleanValue": i.isCustom],
                ]]]
            }]],
            "updatedAt": Self.time(at),
            "device": Self.str(device),
        ])
    }

    /// Claims a scheduled moment for this device ("first device wins"): a compare-and-set on one document, so of two
    /// devices ticking together exactly one gets it. False when another device already holds this slot.
    public func claimSlot(_ tokens: FirebaseTokens, slotId: String, device: String) async throws -> Bool {
        for _ in 0..<3 {
            var request = URLRequest(url: URL(string: slotURL(tokens.uid))!)
            request.setValue("Bearer \(tokens.idToken)", forHTTPHeaderField: "Authorization")
            let (data, response) = try await raw(request)
            let precondition: String
            if response.statusCode == 404 {
                precondition = "currentDocument.exists=false"
            } else {
                try Self.ensureOK(data, response)
                let doc = try await object(data)
                if let fields = doc["fields"] as? [String: Any], Self.stringField(fields, "slotId") == slotId { return false }
                precondition = "currentDocument.updateTime=" + Self.escape(doc["updateTime"] as? String ?? "")
            }
            do {
                try await writeDocument("\(slotURL(tokens.uid))?\(precondition)", tokens, [
                    "slotId": Self.str(slotId),
                    "device": Self.str(device),
                    "at": Self.time(Date()),
                ])
                return true
            } catch let error as FirebaseError where error.failure == .notFound || error.failure == .rejected {
                // Another device wrote it between the read and the write: look again.
            }
        }
        return false
    }

    // MARK: - Favorites

    /// Writes one favorite (or un-favorite: the document stays, with favorite = false, so the change syncs).
    public func setFavorite(_ tokens: FirebaseTokens, _ f: SyncedFavorite, device: String) async throws {
        var fields: [String: Any] = [
            "photoId": Self.str(f.photoId),
            "imageUrl": Self.str(f.imageUrl),
            "pageUrl": Self.str(f.pageUrl),
            "photographer": Self.str(f.photographer),
            "photographerUrl": Self.str(f.photographerUrl),
            "favorite": ["booleanValue": f.favorite],
            "updatedAt": Self.time(f.updatedAt),
            "device": Self.str(device),
        ]
        if let d = f.description, !d.trimmingCharacters(in: .whitespaces).isEmpty { fields["description"] = Self.str(d) }
        try await writeDocument("\(Self.documentsURL)/users/\(tokens.uid)/favorites/\(Self.escape(f.photoId))", tokens, fields)
    }

    /// The favorites changed since `since` (all of them when nil).
    public func favorites(since: Date?, _ tokens: FirebaseTokens) async throws -> [SyncedFavorite] {
        var query: [String: Any] = ["from": [["collectionId": "favorites"]]]
        if let since {
            query["where"] = ["fieldFilter": [
                "field": ["fieldPath": "updatedAt"], "op": "GREATER_THAN", "value": ["timestampValue": Self.timestamp(since)],
            ]]
        }
        var request = URLRequest(url: URL(string: "\(Self.documentsURL)/users/\(tokens.uid):runQuery")!)
        request.httpMethod = "POST"
        request.setValue("Bearer \(tokens.idToken)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: ["structuredQuery": query])
        let data = try await send(request)
        guard let rows = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]] else { return [] }
        return rows.compactMap { row in
            guard let f = (row["document"] as? [String: Any])?["fields"] as? [String: Any],
                  let id = Self.stringField(f, "photoId"), !id.isEmpty, let image = Self.stringField(f, "imageUrl") else { return nil }
            return SyncedFavorite(photoId: id, imageUrl: image, pageUrl: Self.stringField(f, "pageUrl") ?? "",
                                  photographer: Self.stringField(f, "photographer") ?? "",
                                  photographerUrl: Self.stringField(f, "photographerUrl") ?? "",
                                  description: Self.stringField(f, "description"),
                                  favorite: (f["favorite"] as? [String: Any])?["booleanValue"] as? Bool ?? false,
                                  updatedAt: Self.timeField(f, "updatedAt") ?? Date())
        }
    }

    // MARK: - Following and sharing

    /// Who a follow code belongs to, or nil when there is no such code.
    public func followCode(_ tokens: FirebaseTokens, _ code: String) async throws -> (uid: String, name: String, picture: String)? {
        guard let f = try await getDocument("\(Self.documentsURL)/codes/\(code)", tokens) else { return nil }
        return (Self.stringField(f, "uid") ?? "", Self.stringField(f, "name") ?? "", Self.stringField(f, "picture") ?? "")
    }

    /// Claims a new follow code for this account. Fails if the code is taken (a new one is tried).
    public func createFollowCode(_ tokens: FirebaseTokens, _ code: String, name: String, picture: String) async throws {
        try await writeDocument("\(Self.documentsURL)/codes/\(code)?currentDocument.exists=false", tokens, [
            "uid": Self.str(tokens.uid),
            "name": Self.str(name),
            "picture": Self.str(picture),
            "createdAt": Self.time(Date()),
        ])
    }

    static let photoFieldNames = ["photoId", "photoPath", "photographer", "photographerHandle"]

    static func photoFields(_ p: SharedPhoto) -> [String: Any] {
        ["photoId": str(p.id), "photoPath": str(p.path), "photographer": str(p.photographer), "photographerHandle": str(p.handle)]
    }

    /// The wallpaper the follow page shows: the owner's current one, updated as it changes.
    public func setFollowCodePhoto(_ tokens: FirebaseTokens, _ code: String, _ photo: SharedPhoto) async throws {
        let mask = Self.photoFieldNames.map { "&updateMask.fieldPaths=\($0)" }.joined()
        try await writeDocument("\(Self.documentsURL)/codes/\(code)?currentDocument.exists=true\(mask)", tokens, Self.photoFields(photo))
    }

    /// A shared wallpaper's `shares/{id}` document: the photo, and the sender's first name and picture.
    public func createShare(_ tokens: FirebaseTokens, id: String, from: String, picture: String, _ photo: SharedPhoto) async throws {
        var fields = Self.photoFields(photo)
        fields["uid"] = Self.str(tokens.uid)
        fields["from"] = Self.str(from)
        fields["picture"] = Self.str(picture)
        fields["createdAt"] = Self.time(Date())
        try await writeDocument("\(Self.documentsURL)/shares/\(id)?currentDocument.exists=false", tokens, fields)
    }

    public func deleteFollowCode(_ tokens: FirebaseTokens, _ code: String) async throws {
        try await deleteDocument("\(Self.documentsURL)/codes/\(code)", tokens)
    }

    /// Registers this account as a follower of `friendUid`, proven by their code.
    public func addFollower(_ tokens: FirebaseTokens, friendUid: String, code: String, name: String) async throws {
        try await writeDocument("\(Self.documentsURL)/users/\(friendUid)/followers/\(tokens.uid)", tokens, [
            "code": Self.str(code),
            "name": Self.str(name),
            "createdAt": Self.time(Date()),
        ])
    }

    public func removeFollower(_ tokens: FirebaseTokens, owner: String, follower: String) async throws {
        try await deleteDocument("\(Self.documentsURL)/users/\(owner)/followers/\(follower)", tokens)
    }

    /// The accounts following this one, newest first.
    public func followers(_ tokens: FirebaseTokens) async throws -> [Follower] {
        var request = URLRequest(url: URL(string:
            "\(Self.documentsURL)/users/\(tokens.uid)/followers?pageSize=300&mask.fieldPaths=name&mask.fieldPaths=createdAt")!)
        request.setValue("Bearer \(tokens.idToken)", forHTTPHeaderField: "Authorization")
        let doc = try await object(try await send(request))
        let docs = doc["documents"] as? [[String: Any]] ?? []
        return docs.map { d in
            let fields = d["fields"] as? [String: Any] ?? [:]
            let uid = (d["name"] as? String ?? "").split(separator: "/").last.map(String.init) ?? ""
            let name = Self.stringField(fields, "name")?.trimmingCharacters(in: .whitespaces)
            return Follower(uid: uid, name: (name?.isEmpty ?? true) ? "A friend" : name!, since: Self.timeField(fields, "createdAt"))
        }
        .sorted { ($0.since ?? .distantPast) > ($1.since ?? .distantPast) }
    }

    // MARK: - Plumbing

    private func currentURL(_ uid: String) -> String { "\(Self.documentsURL)/users/\(uid)/sync/current" }
    private func sessionURL(_ uid: String, _ sessionId: String) -> String { "\(Self.documentsURL)/users/\(uid)/sessions/\(sessionId)" }

    /// A document's fields, or nil when it doesn't exist.
    private func getDocument(_ url: String, _ tokens: FirebaseTokens) async throws -> [String: Any]? {
        var request = URLRequest(url: URL(string: url)!)
        request.setValue("Bearer \(tokens.idToken)", forHTTPHeaderField: "Authorization")
        let (data, response) = try await raw(request)
        if response.statusCode == 404 { return nil }
        try Self.ensureOK(data, response)
        return try await object(data)["fields"] as? [String: Any] ?? [:]
    }

    private func deleteDocument(_ url: String, _ tokens: FirebaseTokens) async throws {
        var request = URLRequest(url: URL(string: url)!)
        request.httpMethod = "DELETE"
        request.setValue("Bearer \(tokens.idToken)", forHTTPHeaderField: "Authorization")
        let (data, response) = try await raw(request)
        if response.statusCode != 404 { try Self.ensureOK(data, response) }
    }

    private func writeDocument(_ url: String, _ tokens: FirebaseTokens, _ fields: [String: Any]) async throws {
        _ = try await sendJSON("PATCH", url, ["fields": fields], tokens: tokens)
    }

    private func sendJSON(_ method: String, _ url: String, _ body: [String: Any], tokens: FirebaseTokens? = nil) async throws -> [String: Any] {
        var request = URLRequest(url: URL(string: url)!)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if let tokens { request.setValue("Bearer \(tokens.idToken)", forHTTPHeaderField: "Authorization") }
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        return try await object(try await send(request))
    }

    private func send(_ request: URLRequest) async throws -> Data {
        let (data, response) = try await raw(request)
        try Self.ensureOK(data, response)
        return data
    }

    private func raw(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        do {
            return try await http.send(request)
        } catch is CancellationError {
            throw CancellationError()
        } catch {
            throw FirebaseError(.network, "Couldn't reach Google. Check your connection.")
        }
    }

    private func object(_ data: Data) async throws -> [String: Any] {
        guard !data.isEmpty else { return [:] }
        guard let o = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw FirebaseError(.other, "Unexpected response from Google.")
        }
        return o
    }

    static func ensureOK(_ data: Data, _ response: HTTPURLResponse) throws {
        guard !(200..<300).contains(response.statusCode) else { return }
        let message = errorMessage(data)
        var failure: FirebaseFailure = switch response.statusCode {
        case 404: .notFound
        case 429: .other
        case 400..<500: .rejected
        default: .other
        }
        // A conditional write on a missing document is reported as 400 FAILED_PRECONDITION or 404 depending on the API.
        if message.contains("NOT_FOUND") || message.contains("FAILED_PRECONDITION") { failure = .notFound }
        throw FirebaseError(failure, message)
    }

    static func errorMessage(_ data: Data) -> String {
        guard let o = try? JSONSerialization.jsonObject(with: data) as? [String: Any], let error = o["error"] as? [String: Any] else {
            return "Unexpected response from Google."
        }
        return "\(error["status"].map { "\($0)" } ?? "") \(error["message"].map { "\($0)" } ?? "")".trimmingCharacters(in: .whitespaces)
    }

    static func str(_ value: String) -> [String: Any] { ["stringValue": value] }
    static func time(_ value: Date) -> [String: Any] { ["timestampValue": timestamp(value)] }
    static func timestamp(_ value: Date) -> String { ISO8601.format(value) }

    static func stringField(_ fields: [String: Any], _ name: String) -> String? {
        (fields[name] as? [String: Any])?["stringValue"] as? String
    }

    static func timeField(_ fields: [String: Any], _ name: String) -> Date? {
        ((fields[name] as? [String: Any])?["timestampValue"] as? String).flatMap(ISO8601.parse)
    }

    static func tokens(_ idToken: String?, _ refreshToken: String?, _ expiresIn: Any?, _ uid: String) -> FirebaseTokens {
        let seconds = (expiresIn as? String).flatMap(Double.init) ?? (expiresIn as? Double) ?? 3600
        return FirebaseTokens(idToken: idToken ?? "", refreshToken: refreshToken ?? "", expiresAt: Date().addingTimeInterval(seconds), uid: uid)
    }

    /// Escapes a value for a URL query or form body (like .NET's Uri.EscapeDataString).
    static func escape(_ s: String) -> String {
        var allowed = CharacterSet.alphanumerics
        allowed.insert(charactersIn: "-._~")
        return s.addingPercentEncoding(withAllowedCharacters: allowed) ?? s
    }
}
