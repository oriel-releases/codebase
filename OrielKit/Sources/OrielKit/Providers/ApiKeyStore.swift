import Foundation

public protocol ApiKeyStore: Sendable {
    var key: String? { get }
}

public extension ApiKeyStore {
    var hasKey: Bool { key != nil }
}

/// The Unsplash access key that ships with the app, embedded in Info.plist at build time from
/// UNSPLASH_ACCESS_KEY (see scripts/configure.sh). Users never need their own.
public struct BuiltInApiKeyStore: ApiKeyStore {
    public static let infoKey = "OrielUnsplashAccessKey"

    public let key: String?

    public init(bundle: Bundle = .main) {
        let value = (bundle.object(forInfoDictionaryKey: Self.infoKey) as? String)?
            .trimmingCharacters(in: .whitespacesAndNewlines)
        key = value?.isEmpty == false ? value : nil
    }
}
