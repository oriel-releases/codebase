import Foundation
import Synchronization
@testable import OrielKit

/// A fresh folder per test, removed afterwards.
final class TempDir {
    let url = FileManager.default.temporaryDirectory
        .appending(path: "oriel-tests/\(UUID().uuidString)", directoryHint: .isDirectory)

    init() {
        try! FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
    }

    deinit {
        try? FileManager.default.removeItem(at: url)
    }

    var paths: AppPaths {
        AppPaths(dataRoot: url.appending(path: "data", directoryHint: .isDirectory),
                 picturesRoot: url.appending(path: "pics", directoryHint: .isDirectory))
    }
}

struct FakeClock: OrielClock {
    var now: Date
    var calendar: Calendar

    init(_ now: Date, timeZone: TimeZone = .gmt) {
        self.now = now
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        self.calendar = calendar
    }
}

struct FakeKeyStore: ApiKeyStore {
    var key: String?
}

final class FakeSetter: WallpaperSetter {
    struct Call: Equatable {
        var screen: String?
        var file: URL
        var fit: WallpaperFit
    }

    private let state = Mutex<(screens: [ScreenInfo], calls: [Call])>((
        [ScreenInfo(id: "SCREEN1", frame: PixelRect(x: 0, y: 0, width: 2560, height: 1440), isMain: true)], []))

    var calls: [Call] { state.withLock { $0.calls } }

    func screens() -> [ScreenInfo] { state.withLock { $0.screens } }

    func setForAll(_ file: URL, fit: WallpaperFit) throws {
        state.withLock { $0.calls.append(Call(screen: nil, file: file, fit: fit)) }
    }

    func set(_ file: URL, on screenID: String, fit: WallpaperFit) throws {
        state.withLock { $0.calls.append(Call(screen: screenID, file: file, fit: fit)) }
    }

    func setSpanning(_ file: URL) throws {
        state.withLock { $0.calls.append(Call(screen: "span", file: file, fit: .span)) }
    }

    func addScreen(_ id: String, x: Int, width: Int = 1920, height: Int = 1080) {
        state.withLock { $0.screens.append(ScreenInfo(id: id, frame: PixelRect(x: x, y: 0, width: width, height: height), isMain: false)) }
    }

    func current(on screenID: String?) -> URL? {
        state.withLock { $0.calls.last { $0.screen == screenID }?.file }
    }
}

/// Answers HTTP requests from a closure and remembers them.
final class StubHTTP: HTTPClient {
    typealias Responder = @Sendable (URLRequest) throws -> (Data, HTTPURLResponse)

    private let respond: Responder
    private let log = Mutex<[URLRequest]>([])

    init(_ respond: @escaping Responder) {
        self.respond = respond
    }

    var requests: [URLRequest] { log.withLock { $0 } }

    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        log.withLock { $0.append(request) }
        return try respond(request)
    }

    static func json(_ body: String, status: Int = 200, remaining: Int? = 49, for request: URLRequest) -> (Data, HTTPURLResponse) {
        var headers = ["Content-Type": "application/json"]
        if let remaining { headers["X-Ratelimit-Remaining"] = String(remaining) }
        return (Data(body.utf8), HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: headers)!)
    }
}

enum Fixtures {
    static func photo(_ id: String, width: Int = 6000, height: Int = 4000, likes: Int = 100, color: String? = nil) -> PhotoCandidate {
        PhotoCandidate(
            provider: "unsplash", id: id, width: width, height: height, likes: likes, color: color,
            rawURL: URL(string: "https://images.unsplash.com/\(id)?ixid=x")!,
            thumbnailURL: URL(string: "https://t/\(id)")!,
            photoPageURL: URL(string: "https://unsplash.com/photos/\(id)")!,
            photographerName: "Ann",
            photographerURL: URL(string: "https://unsplash.com/@ann")!,
            downloadTrackingURL: URL(string: "https://api.unsplash.com/photos/\(id)/download")!)
    }

    static func entry(_ id: String, at date: Date, file: URL) -> HistoryEntry {
        HistoryEntry(provider: "unsplash", photoID: id, filePath: file.path(percentEncoded: false), thumbnailURL: "t",
                     photoPageURL: "p", photographerName: "n", photographerURL: "u", appliedAt: date)
    }

    /// A file with some bytes in it.
    @discardableResult
    static func file(_ name: String, in folder: URL) -> URL {
        try! FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let url = folder.appending(path: name)
        try! Data("jpg".utf8).write(to: url)
        return url
    }
}

extension Date {
    /// A fixed moment in UTC.
    static func utc(_ year: Int, _ month: Int, _ day: Int, _ hour: Int = 0) -> Date {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = .gmt
        return c.date(from: DateComponents(year: year, month: month, day: day, hour: hour))!
    }
}

/// Hands out queued search results; downloads return a tiny valid JPEG header.
actor FakeProvider: ImageProvider {
    private var responses: [[PhotoCandidate]]
    private(set) var queries: [String] = []
    private(set) var tracked: [String] = []
    var failWith: ProviderError?

    init(_ responses: [[PhotoCandidate]] = []) {
        self.responses = responses
    }

    nonisolated var name: String { "unsplash" }
    nonisolated var isConfigured: Bool { true }
    var remainingRequests: Int? { 40 }

    func fail(with error: ProviderError?) { failWith = error }

    func random(_ query: ImageQuery) async throws -> [PhotoCandidate] {
        queries.append(query.query)
        if let failWith { throw failWith }
        return responses.isEmpty ? [] : responses.removeFirst()
    }

    func search(_ query: String, page: Int) async throws -> SearchPage {
        SearchPage(photos: try await random(ImageQuery(query, targetSize: PixelSize(width: 1, height: 1))), totalPages: 1)
    }

    func photo(id: String) async throws -> PhotoCandidate { Fixtures.photo(id) }

    func download(_ photo: PhotoCandidate, size: PixelSize) async throws -> Data {
        var bytes = Data(count: 4096)
        bytes[0] = 0xFF
        bytes[1] = 0xD8
        return bytes
    }

    func trackUsage(_ photo: PhotoCandidate) async { tracked.append(photo.id) }
}
