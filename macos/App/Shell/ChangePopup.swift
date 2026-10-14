import AppKit
import OrielKit
import SwiftUI

/// A small dark pill that rises from the bottom of the screen while a change from the menu bar or the shortcut is
/// under way. It never takes focus, never shows in Cmd+Tab or Mission Control, and ignores the mouse.
@MainActor
final class ChangePopup {
    private let panel: NSPanel
    private let model = PopupModel()
    private var shownAt = Date()
    private let rest: NSRect
    private let hidden: NSRect

    init() {
        // Wide enough for the longest word, so the pill never changes size between words.
        let longest = ApplyPhase.allWords.map { ($0 as NSString).size(withAttributes: [.font: NSFont.systemFont(ofSize: 13)]).width }.max() ?? 120
        let size = NSSize(width: ceil(longest) + 16 + 10 + 36, height: 40)
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        rest = NSRect(x: screen.midX - size.width / 2, y: screen.minY + 20, width: size.width, height: size.height)
        hidden = rest.offsetBy(dx: 0, dy: -(size.height + 20))

        panel = NSPanel(contentRect: hidden, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .transient, .ignoresCycle]
        panel.hidesOnDeactivate = false
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.ignoresMouseEvents = true

        let blur = NSVisualEffectView()
        blur.material = .hudWindow
        blur.blendingMode = .behindWindow
        blur.state = .active
        blur.appearance = NSAppearance(named: .darkAqua)
        blur.wantsLayer = true
        blur.layer?.cornerRadius = 20
        blur.layer?.masksToBounds = true
        let host = NSHostingView(rootView: PopupContent(model: model))
        host.frame = blur.bounds
        host.autoresizingMask = [.width, .height]
        blur.addSubview(host)
        panel.contentView = blur
    }

    func show() {
        model.word = ApplyPhase.finding.text
        shownAt = Date()
        panel.alphaValue = 0
        panel.setFrame(hidden, display: false)
        panel.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { context in
            context.duration = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? 0 : 0.24
            context.timingFunction = CAMediaTimingFunction(controlPoints: 0.215, 0.61, 0.355, 1)
            panel.animator().setFrame(rest, display: true)
            panel.animator().alphaValue = 1
        }
    }

    func set(_ phase: ApplyPhase) {
        guard phase != .done else { return }
        model.word = phase.text
    }

    /// Stays at least 700 ms in all, then slides back down.
    func dismiss() async {
        let left = 0.7 - Date().timeIntervalSince(shownAt)
        if left > 0 { try? await Task.sleep(for: .seconds(left)) }
        await NSAnimationContext.runAnimationGroup { context in
            context.duration = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? 0 : 0.24
            context.timingFunction = CAMediaTimingFunction(controlPoints: 0.55, 0.055, 0.675, 0.19)
            panel.animator().setFrame(hidden, display: true)
            panel.animator().alphaValue = 0
        }
        panel.orderOut(nil)
    }
}

@MainActor
@Observable
private final class PopupModel {
    var word = ""
}

private struct PopupContent: View {
    var model: PopupModel

    var body: some View {
        HStack(spacing: 10) {
            ProgressView().controlSize(.small).tint(.white)
            SlidingText(text: model.word)
                .font(.system(size: 13))
                .foregroundStyle(.white)
        }
        .padding(.horizontal, 18)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color(hex: 0x141414).opacity(0.35))
        .environment(\.colorScheme, .dark)
    }
}
