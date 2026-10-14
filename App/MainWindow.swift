import SwiftUI
import OrielKit

enum Page: String, CaseIterable, Identifiable {
    case home, history, interests, settings

    var id: Self { self }

    var title: String {
        switch self {
        case .home: "Home"
        case .history: "History"
        case .interests: "Interests"
        case .settings: "Settings"
        }
    }

    var icon: String {
        switch self {
        case .home: "house"
        case .history: "clock.arrow.circlepath"
        case .interests: "square.grid.2x2"
        case .settings: "gearshape"
        }
    }
}

struct MainWindow: View {
    static let id = "main"

    @State private var page: Page = .home

    var body: some View {
        NavigationSplitView {
            List(Page.allCases, selection: $page) { page in
                Label(page.title, systemImage: page.icon)
            }
            .navigationSplitViewColumnWidth(min: 180, ideal: 200)
        } detail: {
            placeholder(for: page)
                .navigationTitle(page.title)
        }
        .frame(minWidth: 760, minHeight: 520)
        // A menu bar app: show in the Dock and Cmd+Tab only while the window is open.
        .onAppear { NSApp.setActivationPolicy(.regular) }
        .onDisappear { NSApp.setActivationPolicy(.accessory) }
    }

    /// Stand-ins until the real pages land in M3.
    @ViewBuilder
    private func placeholder(for page: Page) -> some View {
        switch page {
        case .home:
            ContentUnavailableView {
                Label("Oriel", systemImage: "photo.on.rectangle.angled")
            } description: {
                Text(BuiltInApiKeyStore().hasKey
                     ? "A fresh photo on your desktop every day. Coming soon."
                     : "No Unsplash key in this build: add it to .env and run scripts/configure.sh.")
            }
        default:
            ContentUnavailableView(page.title, systemImage: page.icon, description: Text("Coming soon."))
        }
    }
}
