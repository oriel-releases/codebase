import OrielKit
import SwiftUI

struct SignInView: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        let signIn = app.signIn
        VStack(spacing: 12) {
            Logo(size: 56)
            Text("Sign in to Oriel").pageTitle().padding(.top, 12).padding(.bottom, 8)
            if let error = signIn.error {
                InfoBar(banner: Banner(severity: .error, title: "", message: error, closable: false))
            }
            Button {
                signIn.google()
            } label: {
                HStack(spacing: 10) {
                    Image("GoogleG").resizable().frame(width: 18, height: 18)
                    Text(signIn.isCompleting ? "Continuing with Google…" : "Continue with Google").fontWeight(.medium)
                }
                .frame(maxWidth: .infinity)
            }
            .buttonStyle(GoogleButtonStyle())
            .disabled(signIn.isCompleting)
            Button("Use email and password instead") { signIn.email() }
                .buttonStyle(TextLinkStyle())
                .font(.system(size: 13))
            if signIn.isWaiting {
                Text("Waiting for you to finish in the browser…").font(.system(size: 13)).foregroundStyle(Color.muted)
            }
            Button {
                app.open(OrielLinks.support)
            } label: {
                Label("Need help? Message support on Telegram", systemImage: "bubble.left")
            }
            .buttonStyle(TextLinkStyle(color: .muted))
            .font(.system(size: 12))
            .padding(.top, 12)
        }
        .frame(maxWidth: 360)
        .padding(24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color.paper)
    }
}

/// Google's button: the same light colors in both themes.
private struct GoogleButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        Styled(configuration: configuration)
    }

    private struct Styled: View {
        let configuration: Configuration
        @State private var hover = false
        @Environment(\.isEnabled) private var enabled

        var body: some View {
            configuration.label
                .font(.system(size: 14))
                .foregroundStyle(Color(hex: enabled ? 0x141414 : 0x6B6B6B))
                .frame(height: 44)
                .background(Color(hex: configuration.isPressed ? 0xD8D8D8 : hover ? 0xE4E4E4 : 0xF2F2F2), in: Capsule())
                .contentShape(Capsule())
                .onHover { hover = $0 }
        }
    }
}

// MARK: - Onboarding

struct OnboardingView: View {
    @Environment(AppModel.self) private var app
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let tour = app.onboarding
        GeometryReader { geo in
            ScrollView {
                VStack(spacing: 28) {
                    header(tour)
                    Group {
                        switch tour.step {
                        case 0: WelcomeStep()
                        case 1: InterestsStep()
                        case 2: ScheduleStep()
                        case 3: ChoicesStep()
                        default: DoneStep()
                        }
                    }
                    .id(tour.step)
                    .transition(reduceMotion ? .opacity : .asymmetric(
                        insertion: .offset(x: tour.forward ? 28 : -28).combined(with: .opacity),
                        removal: .opacity))
                    .frame(maxHeight: .infinity, alignment: .center)
                    footer(tour)
                }
                .frame(width: min(640, geo.size.width - 64))
                .frame(minHeight: max(560, geo.size.height - 56))
                .padding(.vertical, 28)
                .frame(maxWidth: .infinity)
            }
        }
        .background(Color.paper)
        .animation(.timingCurve(0.215, 0.61, 0.355, 1, duration: 0.32), value: tour.step)
    }

    @ViewBuilder
    private func header(_ tour: OnboardingModel) -> some View {
        HStack(spacing: 14) {
            if (1...3).contains(tour.step) {
                Text("\(tour.step)/3").font(.system(size: 13, weight: .semibold)).accessibilityLabel("Step \(tour.step) of 3")
                GeometryReader { g in
                    ZStack(alignment: .leading) {
                        Capsule().fill(Color.line)
                        Capsule().fill(Color.ink).frame(width: g.size.width * CGFloat(tour.step) / 3)
                            .animation(.timingCurve(0.215, 0.61, 0.355, 1, duration: 0.45), value: tour.step)
                    }
                }
                .frame(height: 4)
            } else {
                Spacer()
            }
            if tour.step <= 3 {
                Button("Skip setup") { app.skipOnboarding() }.buttonStyle(TextLinkStyle(color: .muted)).font(.system(size: 13))
            }
        }
        .frame(minHeight: 32)
    }

    @ViewBuilder
    private func footer(_ tour: OnboardingModel) -> some View {
        HStack {
            if (1...3).contains(tour.step) {
                Button {
                    tour.go(tour.step - 1)
                } label: {
                    Label("Back", systemImage: "chevron.left").labelStyle(.titleAndIcon)
                }
                .buttonStyle(PillButtonStyle(height: 40, minWidth: 96))
            }
            Spacer()
            if tour.step <= 3 {
                Button {
                    tour.go(tour.step + 1)
                } label: {
                    HStack(spacing: 6) {
                        Text(tour.step == 0 ? "Get started" : "Next")
                        Image(systemName: "chevron.right").font(.system(size: 12))
                    }
                }
                .buttonStyle(PrimaryButtonStyle(height: 40, minWidth: 140))
                .disabled(tour.step == 1 && tour.picked.isEmpty)
                .keyboardShortcut(.defaultAction)
            } else {
                Button("Just take me in") { app.finishOnboarding(changeNow: false) }.buttonStyle(PillButtonStyle(height: 40))
                Button {
                    app.finishOnboarding(changeNow: true)
                } label: {
                    Label(app.shared.isOpen ? "See the shared wallpaper" : "Set my first wallpaper", systemImage: "arrow.clockwise")
                }
                .buttonStyle(PrimaryButtonStyle(height: 40))
                .keyboardShortcut(.defaultAction)
            }
        }
    }
}

private struct WelcomeStep: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 26) {
            // Columns 1 : 1.3 : 1; the side photos sit a little lower, a gentle arc.
            GeometryReader { g in
                let unit = (g.size.width - 20) / 3.3
                HStack(alignment: .top, spacing: 10) {
                    hero("mountains").frame(width: unit, height: 150).padding(.top, 18)
                    hero("northern lights").frame(width: unit * 1.3, height: 168)
                    hero("ocean").frame(width: unit, height: 150).padding(.top, 18)
                }
            }
            .frame(height: 168)
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 12) {
                    Logo(size: 36)
                    Text("Welcome to Oriel").pageTitle()
                }
                Text("A beautiful new wallpaper, picked for you. Setting up takes under a minute.").foregroundStyle(Color.muted)
            }
            VStack(alignment: .leading, spacing: 14) {
                line("calendar", "A new photo, on your schedule", "Every day or every few hours, or whenever you press Change now.")
                line("heart", "Only what you love", "You pick the interests. Star a favorite to keep it forever.")
                line("moon", "Quiet and light", "Oriel waits in the menu bar and only wakes up to change your wallpaper.")
            }
        }
    }

    private func hero(_ interest: String) -> some View {
        Photo(url: InterestCovers.all[interest]?.url(width: 600).absoluteString, maxPixels: 720, placeholder: .surface)
            .clipShape(.rounded(Radius.card))
    }

    private func line(_ icon: String, _ title: String, _ detail: String) -> some View {
        HStack(spacing: 14) {
            Image(systemName: icon).font(.system(size: 16)).frame(width: 40, height: 40).background(Color.surface, in: .rounded(12))
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.system(size: 14, weight: .semibold))
                Text(detail).font(.system(size: 13)).foregroundStyle(Color.muted)
            }
        }
    }
}

private struct InterestsStep: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        let tour = app.onboarding
        VStack(alignment: .leading, spacing: 18) {
            VStack(alignment: .leading, spacing: 6) {
                Text("What do you like?").pageTitle()
                Text("Pick a few. Each new wallpaper comes from one of them.").foregroundStyle(Color.muted)
            }
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 140), spacing: 10)], spacing: 10) {
                ForEach(OrielSettings.builtInTopics, id: \.self) { topic in
                    InterestTile(label: topic.capitalizedFirst, cover: InterestCovers.all[topic]?.url(width: 400).absoluteString,
                                 credit: nil, on: tour.picked.contains(topic), height: 92, radius: 14) {
                        if tour.picked.contains(topic) { tour.picked.remove(topic) } else { tour.picked.insert(topic) }
                    }
                }
            }
            HStack {
                Text(tour.hint).font(.system(size: 13)).foregroundStyle(Color.muted)
                Spacer()
                Button("Select all") { tour.picked = Set(OrielSettings.builtInTopics) }.buttonStyle(PillButtonStyle(height: 32))
                Button("Clear") { tour.picked = [] }.buttonStyle(PillButtonStyle(height: 32))
            }
        }
    }
}

private struct ScheduleStep: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        @Bindable var tour = app.onboarding
        VStack(alignment: .leading, spacing: 18) {
            VStack(alignment: .leading, spacing: 6) {
                Text("How often should it change?").pageTitle()
                Text("Most people like one new photo a day. You can change this any time.").foregroundStyle(Color.muted)
            }
            VStack(spacing: 8) {
                ForEach(OnboardingModel.intervals, id: \.minutes) { option in
                    let chosen = tour.intervalMinutes == option.minutes
                    VStack(spacing: 0) {
                        Button {
                            tour.intervalMinutes = option.minutes
                        } label: {
                            HStack {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(option.label).font(.system(size: 14, weight: .semibold))
                                    Text(option.description).font(.system(size: 13)).foregroundStyle(Color.muted)
                                }
                                Spacer()
                                Radio(on: chosen)
                            }
                            .padding(.horizontal, 18)
                            .padding(.vertical, 12)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        if chosen && option.minutes == 0 {
                            HStack(spacing: 8) {
                                Image(systemName: "clock").font(.system(size: 15)).foregroundStyle(Color.muted)
                                Text("Every day at")
                                Spacer()
                                TimeField(time: $tour.dailyTime)
                            }
                            .padding(EdgeInsets(top: 0, leading: 18, bottom: 14, trailing: 18))
                        }
                    }
                    .background(Color.surface, in: .rounded(Radius.card))
                    .overlay { if chosen { RoundedRectangle(cornerRadius: Radius.card, style: .continuous).strokeBorder(Color.ink, lineWidth: 2) } }
                }
            }
        }
    }
}

private struct Radio: View {
    var on: Bool

    var body: some View {
        ZStack {
            if on {
                Circle().fill(Color.ink)
                Image(systemName: "checkmark").font(.system(size: 11, weight: .bold)).foregroundStyle(Color.paper)
            } else {
                Circle().strokeBorder(Color.muted, lineWidth: 1.5)
            }
        }
        .frame(width: 22, height: 22)
    }
}

private struct ChoicesStep: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        @Bindable var tour = app.onboarding
        VStack(alignment: .leading, spacing: 10) {
            VStack(alignment: .leading, spacing: 6) {
                Text("Make it yours").pageTitle()
                Text("A few choices about how Oriel behaves. All can be changed later in Settings.").foregroundStyle(Color.muted)
            }
            .padding(.bottom, 8)
            card("moon.stars", "Match the time of day", "Darker photos in the evening, brighter ones in the morning.", $tour.matchTimeOfDay)
            card("power", "Open at login", "Oriel waits quietly in the menu bar so your schedule keeps running.", $tour.startAtLogin)
            if tour.displays > 1 {
                card("display.2", "A photo for each display", "Give each display its own photo instead of the same one everywhere.", $tour.photoPerMonitor)
            }
        }
    }

    private func card(_ icon: String, _ title: String, _ detail: String, _ value: Binding<Bool>) -> some View {
        HStack(spacing: 14) {
            Image(systemName: icon).font(.system(size: 16)).frame(width: 40, height: 40).background(Color.paper, in: .rounded(12))
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.system(size: 14, weight: .semibold))
                Text(detail).font(.system(size: 13)).foregroundStyle(Color.muted)
            }
            Spacer()
            Toggle("", isOn: value).toggleStyle(.switch).labelsHidden().tint(Color.ink)
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 14)
        .background(Color.surface, in: .rounded(Radius.card))
    }
}

private struct DoneStep: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        let tour = app.onboarding
        VStack(alignment: .leading, spacing: 22) {
            Image(systemName: "checkmark").font(.system(size: 26, weight: .semibold)).foregroundStyle(Color.paper)
                .frame(width: 64, height: 64).background(Color.ink, in: Circle())
            VStack(alignment: .leading, spacing: 6) {
                Text("You're all set").pageTitle()
                Text("Here's what Oriel will do. Let's find your first wallpaper.").foregroundStyle(Color.muted)
            }
            VStack(spacing: 0) {
                row("heart", "Interests", tour.interestsSummary)
                Rectangle().fill(Color.line).frame(height: 1)
                row("clock", "New photo", tour.scheduleSummary)
                Rectangle().fill(Color.line).frame(height: 1)
                row("display", "Applied to", tour.appliedSummary)
            }
            .padding(.horizontal, 18)
            .padding(.vertical, 6)
            .background(Color.surface, in: .rounded(Radius.card))
            Text(tip).font(.system(size: 13)).foregroundStyle(Color.muted)
        }
    }

    private var tip: String {
        if let key = Hotkey.parse(app.settings.hotkey) {
            return "Tip: press \(key.display) to change your wallpaper from anywhere. Oriel lives in your menu bar."
        }
        return "Oriel lives in your menu bar: open it from there any time."
    }

    private func row(_ icon: String, _ label: String, _ value: String) -> some View {
        HStack(spacing: 12) {
            Image(systemName: icon).font(.system(size: 15)).foregroundStyle(Color.muted).frame(width: 20)
            Text(label).foregroundStyle(Color.muted).frame(width: 110, alignment: .leading)
            Text(value)
            Spacer()
        }
        .padding(.vertical, 10)
    }
}

// MARK: - Shared photo card

struct SharedPhotoView: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        let shared = app.shared
        ZStack {
            Color.black.opacity(0.6).ignoresSafeArea()
            VStack(alignment: .leading, spacing: 0) {
                switch shared.stage {
                case .loading:
                    VStack(spacing: 14) {
                        ProgressView().controlSize(.large)
                        Text("Getting the photo…").foregroundStyle(Color.muted)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 40)
                    .padding(.horizontal, 56)
                case .failed:
                    VStack(alignment: .leading, spacing: 16) {
                        Text("Couldn't open that photo").font(.system(size: 17, weight: .semibold))
                        Text(shared.loadError).foregroundStyle(Color.muted)
                        HStack {
                            Spacer()
                            Button("Close") { shared.close() }.buttonStyle(PillButtonStyle())
                        }
                    }
                    .padding(.vertical, 32)
                    .padding(.horizontal, 36)
                case .ready, .done:
                    if let photo = shared.photo {
                        Photo(url: photo.thumbnailURL.absoluteString, maxPixels: 720, placeholder: .surface).frame(height: 260)
                        VStack(alignment: .leading, spacing: 18) {
                            Text(shared.heading).pageTitle().fixedSize(horizontal: false, vertical: true)
                            Text("\(Text("Photo by ").foregroundStyle(Color.muted))\(Text(photo.photographerName).bold())\(Text(" on Unsplash").foregroundStyle(Color.muted))")
                            if let error = shared.setError {
                                InfoBar(banner: Banner(severity: .warning, title: "", message: error, closable: false))
                            }
                            HStack(spacing: 8) {
                                Spacer()
                                if shared.stage == .done {
                                    Button("Done") { shared.close() }.buttonStyle(PrimaryButtonStyle(height: 38, minWidth: 120))
                                        .keyboardShortcut(.defaultAction)
                                } else {
                                    Button("Not now") { shared.close() }.buttonStyle(PillButtonStyle(height: 38))
                                    Button {
                                        Task { await shared.set() }
                                    } label: {
                                        HStack(spacing: 6) {
                                            if shared.words != nil { ProgressView().controlSize(.mini).tint(Color.paper) }
                                            Text(shared.words ?? "Set as wallpaper")
                                        }
                                    }
                                    .buttonStyle(PrimaryButtonStyle(height: 38, minWidth: 150))
                                    .disabled(shared.words != nil)
                                    .keyboardShortcut(.defaultAction)
                                }
                            }
                        }
                        .padding(EdgeInsets(top: 22, leading: 28, bottom: 26, trailing: 28))
                    }
                }
            }
            .frame(maxWidth: 520)
            .background(Color.paper)
            .clipShape(.rounded(Radius.large))
            .overlay(RoundedRectangle(cornerRadius: Radius.large, style: .continuous).strokeBorder(Color.line, lineWidth: 1))
            .padding(24)
        }
    }
}
