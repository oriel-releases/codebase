import AppKit
import OrielKit
import SwiftUI

/// Lives in the menu bar. The window is destroyed when closed (not just hidden), so its views and photos are freed and
/// the process idles with just the menu bar icon.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, NSMenuDelegate {
    private var sp: Services!
    private(set) var model: AppModel!
    private var window: NSWindow?
    private var statusItem: NSStatusItem!
    private var pauseItem: NSMenuItem!
    private let hotkeys = HotkeyService()
    private var openFriends = false
    private var launchedWithLink = false
    private var observers: [NSObjectProtocol] = []

    // MARK: - Startup

    func applicationWillFinishLaunching(_ notification: Notification) {
        // oriel:// links, at launch and while running.
        NSAppleEventManager.shared().setEventHandler(self, andSelector: #selector(handleURL(_:reply:)),
                                                     forEventClass: AEEventClass(kInternetEventClass), andEventID: AEEventID(kAEGetURL))
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // A second copy started anyway (another bundle): wake the running one and leave.
        let others = NSRunningApplication.runningApplications(withBundleIdentifier: Bundle.main.bundleIdentifier ?? "")
            .filter { $0 != .current }
        if let running = others.first {
            running.activate()
            NSApp.terminate(nil)
            return
        }

        sp = AppHost.build()
        model = AppModel(services: sp)
        model.showWindow = { [weak self] in self?.showWindow() }
        model.isWindowVisible = { [weak self] in self?.window?.isVisible == true }

        buildMainMenu()
        buildStatusItem()

        hotkeys.pressed = { [weak self] in
            Log.app.info("Shortcut pressed")
            Task { await self?.changeFromMenuBar(alwaysShowPopup: true) }
        }
        applyHotkey()

        sp.scheduler.start()
        // The next wallpapers are downloaded ahead of time, so the next change is instant and works offline.
        let wallpapers = sp.wallpapers
        Task {
            await wallpapers.setPrefetchEnabled(true)
            await wallpapers.prefetch()
        }
        // Keep "Open at login" and the launchd fallback in step with the settings, pointed at this copy.
        let scheduleSync = sp.scheduleSync
        Task.detached(priority: .utility) { scheduleSync.sync(executable: AppHost.executable, force: true) }

        sp.auth.start()
        // A wallpaper set on another device shows here within a second; settings and favorites stay in step.
        sp.pulse.start()
        sp.settingsSync.start()
        sp.favoritesSync.start()
        let publisher = sp.syncPublisher
        Task { await publisher.publishLatestIfMissing() }

        observeSystem()
        offerMoveToApplications()

        // Started at login (or with --background): stay in the menu bar. A sign-in link, or not being signed in,
        // always shows the window: nothing works until signed in.
        let background = CommandLine.arguments.contains("--background") || launchedAsLoginItem
        if launchedWithLink || !sp.auth.isSignedIn || !background { showWindow() }
        Log.app.info("Oriel started (background=\(background && sp.auth.isSignedIn && !launchedWithLink))")
    }

    private var launchedAsLoginItem: Bool {
        guard let event = NSAppleEventManager.shared().currentAppleEvent else { return false }
        return event.eventID == kAEOpenApplication
            && event.paramDescriptor(forKeyword: keyAEPropData)?.enumCodeValue == keyAELaunchedAsLogInItem
    }

    /// Opening Oriel again (Finder, Spotlight, the Dock) shows the window.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showWindow()
        return false
    }

    /// Closing the window keeps Oriel running in the menu bar.
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    private func observeSystem() {
        let ws = NSWorkspace.shared.notificationCenter
        let nc = NotificationCenter.default
        let wake: @Sendable (Notification) -> Void = { [weak self] _ in MainActor.assumeIsolated { self?.sp.scheduler.wake() } }
        observers = [
            // After sleep or a clock change: check right away instead of finishing the old wait.
            ws.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main, using: wake),
            nc.addObserver(forName: .NSSystemClockDidChange, object: nil, queue: .main, using: wake),
            nc.addObserver(forName: .NSSystemTimeZoneDidChange, object: nil, queue: .main, using: wake),
            // macOS only changes the current Space: the others get the photo as you visit them; a new display too.
            ws.addObserver(forName: NSWorkspace.activeSpaceDidChangeNotification, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.reapplyOnSpaces() }
            },
            nc.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.reapplyOnSpaces() }
            },
            nc.addObserver(forName: .orielSettingsChanged, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated {
                    self?.applyHotkey()
                    self?.refreshStatusItem()
                }
            },
        ]
        if let current = model.current, !current.filePath.isEmpty, FileManager.default.fileExists(atPath: current.filePath) {
            sp.setter.adopt(current.fileURL, fit: model.settings.fit)
        }
        sp.wallpapers.applied.subscribe { [weak self] result in
            Task { @MainActor in
                guard let self, let first = result.entries.first, first.screenID == nil else { return }
                self.sp.setter.adopt(first.fileURL, fit: self.model.settings.fit)
            }
        }
    }

    private func reapplyOnSpaces() {
        let folders = [model.wallpaperFolder, sp.paths.dataRoot].map { $0.standardizedFileURL.path(percentEncoded: false) }
        sp.setter.reapplyIfNeeded { url in
            let path = url.standardizedFileURL.path(percentEncoded: false)
            return folders.contains { path.hasPrefix($0) }
        }
    }

    private var lastHotkey: String?

    private func applyHotkey() {
        let text = model.settings.hotkey
        guard text != lastHotkey else { return }
        lastHotkey = text
        model.hotkeyError = hotkeys.apply(text)
        Log.app.info("Shortcut registered: \(model.hotkeyError ?? "ok")")
    }

    // MARK: - Links

    @objc private func handleURL(_ event: NSAppleEventDescriptor, reply: NSAppleEventDescriptor) {
        guard let link = event.paramDescriptor(forKeyword: keyDirectObject)?.stringValue else { return }
        if model == nil {
            // Still starting: handled as soon as the services exist.
            launchedWithLink = true
            DispatchQueue.main.async { [weak self] in self?.handle([link]) }
        } else {
            handle([link])
        }
    }

    private func handle(_ links: [String]) {
        guard model != nil, DeepLink.isLink(links) else { return }
        defer { showWindow() }
        if DeepLink.isOpen(links) { return }
        if let shared = DeepLink.sharedPhoto(links) {
            Log.app.info("Shared photo link received: \(shared.id)")
            Task { await model.shared.open(shared.id, from: shared.from) }
            return
        }
        // A friend's follow link (or one carried through sign-in): follow, and show it on the Friends page.
        let follow = DeepLink.followCode(links)
        if let follow {
            Log.app.info("Follow link received")
            openFriends = true
            let service = sp.follow
            Task { await service.followFromLink(follow) }
        }
        if let token = DeepLink.googleToken(links) {
            Task { await model.signIn.complete(googleToken: token) }
        } else if let refresh = DeepLink.sessionToken(links) {
            Task { await model.signIn.complete(refreshToken: refresh) }
        } else if follow == nil {
            model.signIn.invalid()
        }
        // Someone opened a share link, installed Oriel and signed in: the photo they wanted comes along.
        if let pending = DeepLink.pendingShare(links) {
            Task { await model.shared.open(pending.id, from: pending.from) }
        }
    }

    // MARK: - Window

    func showWindow() {
        guard model != nil else { return }
        if window == nil {
            let host = NSHostingController(rootView: RootView().environment(model))
            host.sceneBridgingOptions = [.toolbars, .title]
            let w = OrielWindow(contentViewController: host)
            w.styleMask = [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView]
            w.title = "Oriel (Beta)"
            w.titlebarAppearsTransparent = true
            w.toolbarStyle = .unified
            w.minSize = NSSize(width: 560, height: 480)
            w.isReleasedWhenClosed = false
            w.delegate = self
            let visible = (NSScreen.main ?? NSScreen.screens[0]).visibleFrame
            let size = NSSize(width: min(1100, visible.width), height: min(780, visible.height))
            w.setFrame(NSRect(x: visible.midX - size.width / 2, y: visible.midY - size.height / 2, width: size.width, height: size.height),
                       display: false)
            window = w
        }
        if openFriends {
            openFriends = false
            model.page = .friends
        }
        model.reload()
        NSApp.setActivationPolicy(.regular)
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate()
    }

    func windowDidBecomeKey(_ notification: Notification) {
        model.reload()
    }

    /// The window is gone: back to the menu bar only, and let the photos go.
    func windowWillClose(_ notification: Notification) {
        window = nil
        model.search.release()
        ImageCache.release()
        NSApp.setActivationPolicy(.accessory)
    }

    @objc private func openWindow() { showWindow() }

    @objc private func openSettings() {
        showWindow()
        model.page = .settings
    }

    @objc private func checkForUpdates() { model.updates.check() }

    // MARK: - Menu bar

    private func buildStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(named: "MenuBarIcon")
        statusItem.button?.image?.isTemplate = true
        let menu = NSMenu()
        menu.delegate = self
        menu.addItem(withTitle: "Next wallpaper", action: #selector(nextWallpaper), keyEquivalent: "").target = self
        menu.addItem(withTitle: "Open Oriel", action: #selector(openWindow), keyEquivalent: "").target = self
        pauseItem = menu.addItem(withTitle: "Pause daily changes", action: #selector(togglePause), keyEquivalent: "")
        pauseItem.target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "Quit Oriel", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "")
        statusItem.menu = menu
        refreshStatusItem()
    }

    private func refreshStatusItem() {
        let paused = model.settings.paused
        pauseItem?.state = paused ? .on : .off
        statusItem?.button?.toolTip = paused ? "Oriel (paused)" : "Oriel"
    }

    @objc private func togglePause() { model.setPaused(!model.settings.paused) }

    @objc private func nextWallpaper() {
        Task { await changeFromMenuBar(alwaysShowPopup: false) }
    }

    /// Menu bar or shortcut: change now. The popup says it's working; the open window already shows its own words, so
    /// there it only appears for the shortcut.
    private func changeFromMenuBar(alwaysShowPopup: Bool) async {
        guard sp.auth.isSignedIn else {
            showWindow()
            return
        }
        var popup: ChangePopup?
        var token: Broadcaster<ApplyPhase>.Token?
        if !model.isBusy && (alwaysShowPopup || window?.isVisible != true) {
            let p = ChangePopup()
            p.show()
            popup = p
            token = sp.wallpapers.phases.subscribe { phase in Task { @MainActor in p.set(phase) } }
        }
        // Don't let App Nap slow the change down.
        let activity = ProcessInfo.processInfo.beginActivity(options: .userInitiatedAllowingIdleSystemSleep, reason: "Changing the wallpaper")
        await model.changeNow()
        ProcessInfo.processInfo.endActivity(activity)
        if let token { sp.wallpapers.phases.unsubscribe(token) }
        await popup?.dismiss()
    }

    private func buildMainMenu() {
        let main = NSMenu()
        let appItem = main.addItem(withTitle: "Oriel", action: nil, keyEquivalent: "")
        let appMenu = NSMenu(title: "Oriel")
        appMenu.addItem(withTitle: "About Oriel", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
        appMenu.addItem(withTitle: "Check for Updates…", action: #selector(checkForUpdates), keyEquivalent: "").target = self
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Settings…", action: #selector(openSettings), keyEquivalent: ",").target = self
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Hide Oriel", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(withTitle: "Quit Oriel", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu

        let editItem = main.addItem(withTitle: "Edit", action: nil, keyEquivalent: "")
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "Z")
        edit.addItem(.separator())
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit

        let windowItem = main.addItem(withTitle: "Window", action: nil, keyEquivalent: "")
        let windowMenu = NSMenu(title: "Window")
        windowMenu.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowMenu.addItem(withTitle: "Minimize", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowItem.submenu = windowMenu
        NSApp.windowsMenu = windowMenu
        NSApp.mainMenu = main
    }

    // MARK: - Move to Applications

    /// Opened straight from Downloads (or the disk image), launchd would point at the wrong copy: offer to move it once.
    private func offerMoveToApplications() {
        let path = Bundle.main.bundlePath
        guard !LoginItem.isInstalled, !path.contains("/DerivedData/"), !path.contains("/build/Build/"),
              !UserDefaults.standard.bool(forKey: "askedToMove") else { return }
        UserDefaults.standard.set(true, forKey: "askedToMove")
        let alert = NSAlert()
        alert.messageText = "Move Oriel to your Applications folder?"
        alert.informativeText = "Oriel works best from Applications: it can then open at login and keep your schedule running."
        alert.addButton(withTitle: "Move to Applications")
        alert.addButton(withTitle: "Not now")
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        let target = URL(filePath: "/Applications/Oriel.app")
        do {
            if FileManager.default.fileExists(atPath: target.path(percentEncoded: false)) {
                try FileManager.default.trashItem(at: target, resultingItemURL: nil)
            }
            try FileManager.default.copyItem(at: Bundle.main.bundleURL, to: target)
            try? FileManager.default.trashItem(at: Bundle.main.bundleURL, resultingItemURL: nil)
            // This copy quits first, then the moved one starts (it would otherwise find this one running and leave).
            let relaunch = Process()
            relaunch.executableURL = URL(filePath: "/bin/sh")
            relaunch.arguments = ["-c", "sleep 1; /usr/bin/open /Applications/Oriel.app"]
            try relaunch.run()
            NSApp.terminate(nil)
        } catch {
            Log.app.warning("Couldn't move Oriel to Applications", error)
        }
    }
}

/// Oriel's window. A key that nothing in the window uses (most pages have no text field) is ignored quietly instead
/// of the system's error beep. Text fields, ⌘ shortcuts and Escape are handled before it gets here.
final class OrielWindow: NSWindow {
    override func noResponder(for eventSelector: Selector) {
        if eventSelector == #selector(NSResponder.keyDown(with:)) { return }
        super.noResponder(for: eventSelector)
    }
}
