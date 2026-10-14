import OrielKit
import SwiftUI

/// The window's content: the sidebar and pages, or a cover over them (sign in, the tour, a shared photo, signing out).
struct RootView: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        ZStack {
            if app.isSigningOut {
                SigningOutView()
            } else if !app.isSignedIn {
                SignInView()
            } else if app.showOnboarding {
                OnboardingView()
            } else {
                MainView()
                if app.shared.isOpen { SharedPhotoView() }
            }
        }
        .tint(Color.ink)
        .frame(minWidth: 560, minHeight: 480)
        .background(Color.paper)
        .onAppear { app.evaluateOnboarding() }
    }
}

struct MainView: View {
    @Environment(AppModel.self) private var app
    @State private var pulse: [Page: Int] = [:]

    var body: some View {
        @Bindable var app = app
        NavigationSplitView {
            VStack(spacing: 0) {
                List(selection: Binding(get: { app.page }, set: { if let p = $0 { select(p) } })) {
                    ForEach(Page.allCases.filter { $0 != .settings }) { page in
                        SidebarItem(page: page, pulse: pulse[page] ?? 0).tag(page)
                    }
                }
                List(selection: Binding(get: { app.page }, set: { if let p = $0 { select(p) } })) {
                    SidebarItem(page: .settings, pulse: pulse[.settings] ?? 0).tag(Page.settings)
                }
                .frame(height: 52)
                .scrollDisabled(true)
            }
            .navigationSplitViewColumnWidth(min: 180, ideal: 200, max: 260)
        } detail: {
            Group {
                switch app.page {
                case .home: HomeView()
                case .search: if app.showingInterests { InterestsView() } else { SearchView() }
                case .history, .favorites: HistoryView(favorites: app.page == .favorites)
                case .friends: FriendsView()
                case .settings: SettingsView()
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            .background(Color.paper)
            .id(app.page == .search ? "search-\(app.showingInterests)" : app.page.rawValue)
            .transition(.opacity.combined(with: .offset(y: 6)))
            .animation(.easeOut(duration: 0.2), value: app.page)
        }
        .navigationTitle("Oriel (Beta)")
    }

    private func select(_ page: Page) {
        pulse[page, default: 0] += 1
        // Clicking the current item again leaves a sub-page (Interests).
        if page == app.page && page == .search { app.showingInterests = false }
        app.page = page
    }
}

/// A sidebar item whose icon plays a little animation when clicked.
struct SidebarItem: View {
    var page: Page
    var pulse: Int

    @State private var scale: CGFloat = 1
    @State private var lift: CGFloat = 0
    @State private var rotation: Double = 0
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Label {
            Text(page.title)
        } icon: {
            Image(systemName: page.icon)
                .scaleEffect(scale)
                .offset(y: lift)
                .rotationEffect(.degrees(rotation))
        }
        .onChange(of: pulse) { play() }
    }

    private func play() {
        guard !reduceMotion else { return }
        switch page {
        case .search:
            // The magnifier wiggles.
            withAnimation(.easeInOut(duration: 0.11)) { rotation = -18 } completion: {
                withAnimation(.easeInOut(duration: 0.15)) { rotation = 14 } completion: {
                    withAnimation(.easeInOut(duration: 0.14)) { rotation = 0 }
                }
            }
            withAnimation(.easeInOut(duration: 0.15)) { scale = 1.2 } completion: {
                withAnimation(.easeInOut(duration: 0.25)) { scale = 1 }
            }
        case .history:
            // Winds back a turn.
            rotation = 0
            withAnimation(.timingCurve(0.215, 0.61, 0.355, 1, duration: 0.55)) { rotation = -360 } completion: { rotation = 0 }
        default:
            // Hop and pop.
            withAnimation(.easeInOut(duration: 0.14)) {
                scale = 1.28
                lift = -3
            } completion: {
                withAnimation(.easeInOut(duration: 0.16)) {
                    scale = 1
                    lift = 0
                }
            }
        }
    }
}

struct SigningOutView: View {
    var body: some View {
        VStack(spacing: 16) {
            ProgressView().controlSize(.large)
            Text("Signing out…").foregroundStyle(Color.muted)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color.paper)
    }
}
