import SwiftUI

/// Which part of the window has the keyboard: the sidebar's pages, its pinned Settings item, or the page itself.
enum WindowFocus: Hashable {
    case pages, settings, content
}

/// Arrow keys over a grid of cards: move the highlight (scrolling it into view), act on the highlighted card, and
/// leave to the sidebar with ← from the first column.
struct KeyboardGrid<ID: Hashable>: ViewModifier {
    let ids: [ID]
    let minItemWidth: CGFloat
    let spacing: CGFloat
    @Binding var selected: ID?
    var focus: FocusState<WindowFocus?>.Binding
    let proxy: ScrollViewProxy
    /// Keys besides the arrows (Return, Space, F, Delete…) and what they do with the highlighted card.
    var actions: [KeyEquivalent: (ID) -> Void] = [:]

    @State private var width: CGFloat = 0

    private var columns: Int {
        max(1, Int((width + spacing) / (minItemWidth + spacing)))
    }

    func body(content: Content) -> some View {
        content
            .onGeometryChange(for: CGFloat.self) { $0.size.width } action: { width = $0 }
            .focusable()
            .focusEffectDisabled()
            .focused(focus, equals: .content)
            .onChange(of: focus.wrappedValue) { _, now in
                // Arriving from the sidebar: start on the first card.
                if now == .content, selected == nil || !ids.contains(selected!) { selected = ids.first }
            }
            .onKeyPress(phases: .down) { press in
                guard focus.wrappedValue == .content else { return .ignored }
                return handle(press.key)
            }
    }

    private func handle(_ key: KeyEquivalent) -> KeyPress.Result {
        guard !ids.isEmpty else {
            if key == .leftArrow { focus.wrappedValue = .pages; return .handled }
            return .ignored
        }
        let index = selected.flatMap { ids.firstIndex(of: $0) } ?? 0
        var next = index
        switch key {
        case .leftArrow:
            if index % columns == 0 {
                focus.wrappedValue = .pages
                return .handled
            }
            next = index - 1
        case .rightArrow: next = min(ids.count - 1, index + 1)
        case .upArrow: next = index - columns >= 0 ? index - columns : index
        case .downArrow: next = min(ids.count - 1, index + columns)
        default:
            guard let action = actions[key] else { return .ignored }
            action(ids[index])
            return .handled
        }
        selected = ids[next]
        withAnimation(.easeOut(duration: 0.15)) { proxy.scrollTo(ids[next], anchor: nil) }
        return .handled
    }
}

extension View {
    func keyboardGrid<ID: Hashable>(_ ids: [ID], minItemWidth: CGFloat, spacing: CGFloat, selected: Binding<ID?>,
                                    focus: FocusState<WindowFocus?>.Binding, proxy: ScrollViewProxy,
                                    actions: [KeyEquivalent: (ID) -> Void] = [:]) -> some View {
        modifier(KeyboardGrid(ids: ids, minItemWidth: minItemWidth, spacing: spacing, selected: selected, focus: focus,
                              proxy: proxy, actions: actions))
    }

    /// The ring around the card the keyboard is on.
    func keyboardRing(_ on: Bool, radius: CGFloat) -> some View {
        overlay {
            if on {
                RoundedRectangle(cornerRadius: radius, style: .continuous)
                    .strokeBorder(Color.ink, lineWidth: 3)
                    .padding(-4)
                    .allowsHitTesting(false)
            }
        }
    }
}
