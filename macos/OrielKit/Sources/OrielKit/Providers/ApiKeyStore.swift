import Foundation

public protocol ApiKeyStore: Sendable {
    var key: String? { get }
}

public extension ApiKeyStore {
    var hasKey: Bool { key != nil }
}

/// A key that ships with the app, embedded in Info.plist at build time from .env or the environment (see
/// scripts/configure.sh): the Unsplash access key (UNSPLASH_ACCESS_KEY), or Pollinations' (POLLINATIONS_KEY, for AI
/// wallpapers). Users never need their own.
public struct BuiltInApiKeyStore: ApiKeyStore {
    public static let infoKey = "OrielUnsplashAccessKey"
    public static let pollinationsInfoKey = "OrielPollinationsKey"

    public let key: String?

    /// The Pollinations key (AI wallpapers).
    public static func pollinations(bundle: Bundle = .main) -> BuiltInApiKeyStore {
        BuiltInApiKeyStore(bundle: bundle, infoKey: pollinationsInfoKey)
    }

    public init(bundle: Bundle = .main, infoKey: String = BuiltInApiKeyStore.infoKey) {
        let value = (bundle.object(forInfoDictionaryKey: infoKey) as? String)?
            .trimmingCharacters(in: .whitespacesAndNewlines)
        key = value?.isEmpty == false ? value : nil
    }
}
