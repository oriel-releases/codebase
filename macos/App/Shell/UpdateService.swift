import AppKit
import Observation
import Sparkle

/// Self-updates with Sparkle: checks once a day, downloads in the background, installs on quit (or when the person
/// clicks Install). Only in the installed app, and only when the build carries the update key.
@MainActor
@Observable
final class UpdateService {
    @ObservationIgnored private var controller: SPUStandardUpdaterController?
    let version: String

    init(version: String) {
        self.version = version
        let key = Bundle.main.object(forInfoDictionaryKey: "SUPublicEDKey") as? String ?? ""
        if LoginItem.isInstalled && !key.isEmpty {
            controller = SPUStandardUpdaterController(startingUpdater: true, updaterDelegate: nil, userDriverDelegate: nil)
        }
    }

    var canCheck: Bool { controller?.updater.canCheckForUpdates ?? false }

    var description: String {
        controller == nil
            ? "Version \(version) beta · Updates work in the installed app."
            : "Version \(version) beta · Oriel checks for updates once a day and installs them in the background."
    }

    func check() {
        controller?.checkForUpdates(nil)
    }
}
