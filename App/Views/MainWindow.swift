import SwiftUI

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
    @State private var page: Page = .home

    var body: some View {
        NavigationSplitView {
            List(Page.allCases, selection: $page) { page in
                Label(page.title, systemImage: page.icon)
            }
            .navigationSplitViewColumnWidth(min: 180, ideal: 200)
        } detail: {
            Group {
                switch page {
                case .home: HomeView()
                case .history: HistoryView()
                case .interests: InterestsView()
                case .settings: SettingsView()
                }
            }
            .navigationTitle(page.title)
        }
    }
}
