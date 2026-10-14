import AppKit
import Carbon.HIToolbox
import OrielKit

/// A global shortcut, stored with Windows-style names ("Ctrl+Alt+W"; Alt is Option) and shown with Mac symbols (⌃⌥W).
struct Hotkey: Equatable {
    var control = false
    var option = false
    var shift = false
    var command = false
    /// The key's name: "W", "5", "F5", "Space", "Up"…
    var key: String

    static let keyCodes: [String: Int] = {
        var map: [String: Int] = [
            "A": kVK_ANSI_A, "B": kVK_ANSI_B, "C": kVK_ANSI_C, "D": kVK_ANSI_D, "E": kVK_ANSI_E, "F": kVK_ANSI_F, "G": kVK_ANSI_G,
            "H": kVK_ANSI_H, "I": kVK_ANSI_I, "J": kVK_ANSI_J, "K": kVK_ANSI_K, "L": kVK_ANSI_L, "M": kVK_ANSI_M, "N": kVK_ANSI_N,
            "O": kVK_ANSI_O, "P": kVK_ANSI_P, "Q": kVK_ANSI_Q, "R": kVK_ANSI_R, "S": kVK_ANSI_S, "T": kVK_ANSI_T, "U": kVK_ANSI_U,
            "V": kVK_ANSI_V, "W": kVK_ANSI_W, "X": kVK_ANSI_X, "Y": kVK_ANSI_Y, "Z": kVK_ANSI_Z,
            "0": kVK_ANSI_0, "1": kVK_ANSI_1, "2": kVK_ANSI_2, "3": kVK_ANSI_3, "4": kVK_ANSI_4, "5": kVK_ANSI_5, "6": kVK_ANSI_6,
            "7": kVK_ANSI_7, "8": kVK_ANSI_8, "9": kVK_ANSI_9,
            "Space": kVK_Space, "PageUp": kVK_PageUp, "PageDown": kVK_PageDown, "Home": kVK_Home, "End": kVK_End,
            "Left": kVK_LeftArrow, "Right": kVK_RightArrow, "Up": kVK_UpArrow, "Down": kVK_DownArrow, "Delete": kVK_ForwardDelete,
        ]
        let functionKeys = [kVK_F1, kVK_F2, kVK_F3, kVK_F4, kVK_F5, kVK_F6, kVK_F7, kVK_F8, kVK_F9, kVK_F10, kVK_F11, kVK_F12,
                            kVK_F13, kVK_F14, kVK_F15, kVK_F16, kVK_F17, kVK_F18, kVK_F19, kVK_F20]
        for (i, code) in functionKeys.enumerated() { map["F\(i + 1)"] = code }
        return map
    }()

    static let symbols = ["Space": "Space", "PageUp": "⇞", "PageDown": "⇟", "Home": "↖", "End": "↘",
                          "Left": "←", "Right": "→", "Up": "↑", "Down": "↓", "Delete": "⌦"]

    /// "Ctrl+Alt+W" → a shortcut; nil when empty or not valid.
    static func parse(_ text: String) -> Hotkey? {
        var h = Hotkey(key: "")
        for part in text.split(separator: "+").map({ $0.trimmingCharacters(in: .whitespaces) }) {
            switch part.lowercased() {
            case "ctrl", "control": h.control = true
            case "alt", "option": h.option = true
            case "shift": h.shift = true
            case "cmd", "command", "win": h.command = true
            default: h.key = part.count == 1 ? part.uppercased() : part
            }
        }
        return h.isValid ? h : nil
    }

    /// The next valid combination pressed while recording.
    init?(event: NSEvent) {
        guard let name = Self.keyCodes.first(where: { $0.value == Int(event.keyCode) })?.key else { return nil }
        let flags = event.modifierFlags
        self.init(control: flags.contains(.control), option: flags.contains(.option), shift: flags.contains(.shift),
                  command: flags.contains(.command), key: name)
        guard isValid else { return nil }
    }

    init(control: Bool = false, option: Bool = false, shift: Bool = false, command: Bool = false, key: String) {
        self.control = control
        self.option = option
        self.shift = shift
        self.command = command
        self.key = key
    }

    /// Needs Control, Option or Command (Shift alone isn't enough), so plain typing never triggers it.
    var isValid: Bool { Self.keyCodes[key] != nil && (control || option || command) }

    var storage: String {
        ([control ? "Ctrl" : nil, option ? "Alt" : nil, shift ? "Shift" : nil, command ? "Cmd" : nil, key].compactMap { $0 })
            .joined(separator: "+")
    }

    /// "⌃⌥W"
    var display: String {
        (control ? "⌃" : "") + (option ? "⌥" : "") + (shift ? "⇧" : "") + (command ? "⌘" : "") + (Self.symbols[key] ?? key)
    }

    var carbonModifiers: UInt32 {
        UInt32((control ? controlKey : 0) | (option ? optionKey : 0) | (shift ? shiftKey : 0) | (command ? cmdKey : 0))
    }
}

/// Registers the shortcut with Carbon's RegisterEventHotKey: works while the window is closed and needs no
/// Accessibility permission. One shortcut at a time.
@MainActor
final class HotkeyService {
    private var ref: EventHotKeyRef?
    private var handler: EventHandlerRef?
    var pressed: () -> Void = {}

    /// Registers `text` (replacing the previous one). Returns the problem to show, or nil when it worked or is empty.
    func apply(_ text: String) -> String? {
        if let ref { UnregisterEventHotKey(ref) }
        ref = nil
        guard let key = Hotkey.parse(text) else { return nil }
        installHandler()
        var hotKeyRef: EventHotKeyRef?
        let id = EventHotKeyID(signature: OSType(0x4F52_4C21), id: 1) // "ORL!"
        let status = RegisterEventHotKey(UInt32(Hotkey.keyCodes[key.key]!), key.carbonModifiers, id, GetApplicationEventTarget(), 0, &hotKeyRef)
        guard status == noErr, let hotKeyRef else {
            return "\(key.display) is already used by another app. Pick a different shortcut."
        }
        ref = hotKeyRef
        return nil
    }

    private func installHandler() {
        guard handler == nil else { return }
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let me = Unmanaged.passUnretained(self).toOpaque()
        InstallEventHandler(GetApplicationEventTarget(), { _, _, data in
            guard let data else { return noErr }
            let service = Unmanaged<HotkeyService>.fromOpaque(data).takeUnretainedValue()
            MainActor.assumeIsolated { service.pressed() }
            return noErr
        }, 1, &spec, me, &handler)
    }
}
