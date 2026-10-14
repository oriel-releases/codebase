import OrielKit
import SwiftUI

struct FriendsView: View {
    @Environment(AppModel.self) private var app
    @State private var enteringCode = false
    @State private var showingFollowers = false
    @State private var confirmUnfollow = false
    @State private var confirmStop = false
    @State private var shareAnchor: NSView?

    var body: some View {
        let friends = app.friends
        let sharing = app.settings.myFollowCode != nil
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Friends").pageTitle().padding(.top, 24).padding(.bottom, 4)
                if app.settings.isFollowing { FollowingCard(confirmUnfollow: $confirmUnfollow) }
                if friends.linkCode != nil {
                    HStack(spacing: 10) {
                        ProgressView().controlSize(.small)
                        Text("Following your friend…").foregroundStyle(Color.muted)
                    }
                }
                VStack(spacing: 0) {
                    row(icon: "square.and.arrow.up", title: "Share my wallpaper", detail: shareDetail(sharing), first: true) {
                        if sharing {
                            Button("Turn off") { confirmStop = true }.buttonStyle(PillButtonStyle(height: 36))
                        }
                        Button {
                            Task { await share() }
                        } label: {
                            Text(friends.sharing ? "Getting your link…" : friends.linkCopied ? "Link copied" : "Share")
                        }
                        .buttonStyle(PrimaryButtonStyle(height: 36, minWidth: 96))
                        .background(AnchorView(view: $shareAnchor))
                        .disabled(friends.sharing)
                    }
                    if !app.settings.isFollowing {
                        row(icon: "person.badge.plus", title: "Follow a friend", detail: "Use a friend's wallpaper.") {
                            Button("Enter code") { enteringCode = true }.buttonStyle(PillButtonStyle(height: 36, minWidth: 96))
                        }
                    }
                    if let count = friends.followerCount, count > 0 {
                        row(icon: "person.2", title: "Followers", detail: count == 1 ? "1 person" : "\(count) people") {
                            Button("See all") { showingFollowers = true }.buttonStyle(PillButtonStyle(height: 36))
                        }
                    }
                }
                .background(Color.surface, in: .rounded(Radius.large))
                if let banner = friends.banner { InfoBar(banner: banner) { friends.banner = nil } }
            }
            .pageColumn()
        }
        .onAppear { friends.reload() }
        .sheet(isPresented: $enteringCode) { CodeSheet() }
        .sheet(isPresented: $showingFollowers) { FollowersSheet() }
        .alert("Stop following \(app.settings.followName ?? "your friend")?", isPresented: $confirmUnfollow) {
            Button("Unfollow", role: .destructive) { Task { await friends.unfollow() } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("Oriel goes back to your own wallpapers.")
        }
        .alert("Turn off sharing?", isPresented: $confirmStop) {
            Button("Turn off", role: .destructive) { Task { await friends.stopSharing() } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(stopMessage)
        }
    }

    private var stopMessage: String {
        let n = app.friends.followerCount ?? 0
        return "Your link stops working." + (n == 1 ? " The 1 person following you stops getting your wallpaper."
            : n > 1 ? " The \(n) people following you stop getting your wallpaper." : "")
    }

    private func shareDetail(_ sharing: Bool) -> String {
        guard sharing else { return "Off. Friends get every wallpaper you set." }
        switch app.friends.followerCount {
        case nil: return "On."
        case 0: return "On. No one follows yet."
        case 1: return "On. 1 person follows you."
        case let n?: return "On. \(n) people follow you."
        }
    }

    private func share() async {
        guard let link = await app.friends.shareLink() else { return }
        let text = "Follow my wallpaper on Oriel: whenever I change mine, yours changes too."
        if !SharePicker.show([text, URL(string: link)!], title: "Follow my wallpaper", from: shareAnchor) {
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(link, forType: .string)
            app.friends.linkCopied = true
            try? await Task.sleep(for: .seconds(2))
            app.friends.linkCopied = false
        }
    }

    @ViewBuilder
    private func row<Buttons: View>(icon: String, title: String, detail: String, first: Bool = false,
                                    @ViewBuilder buttons: () -> Buttons) -> some View {
        VStack(spacing: 0) {
            if !first { Rectangle().fill(Color.line).frame(height: 1).padding(.leading, 76).padding(.trailing, 20) }
            HStack(spacing: 16) {
                Image(systemName: icon).font(.system(size: 16)).frame(width: 40, height: 40).background(Color.paper, in: .rounded(12))
                VStack(alignment: .leading, spacing: 2) {
                    Text(title).font(.system(size: 14, weight: .semibold))
                    Text(detail).font(.system(size: 13)).foregroundStyle(Color.muted)
                }
                Spacer()
                HStack(spacing: 8) { buttons() }
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 16)
        }
    }
}

private struct FollowingCard: View {
    @Environment(AppModel.self) private var app
    @Binding var confirmUnfollow: Bool

    var body: some View {
        let name = app.settings.followName ?? "your friend"
        let photo = app.entries.first { $0.topic == "from \(name)" }
        ZStack(alignment: .bottom) {
            Color.tile
            if let photo {
                Photo(file: photo.filePath, url: photo.thumbnailURL, maxPixels: 900, placeholder: .tile)
                Scrim()
            } else {
                ZStack {
                    Circle().fill(.white.opacity(0.2))
                    Text(String(name.prefix(1)).uppercased()).font(.system(size: 32, weight: .semibold)).foregroundStyle(.white)
                    if let picture = app.settings.followPicture, picture.hasPrefix("https://") {
                        Photo(url: picture, maxPixels: 160, placeholder: .clear).clipShape(Circle())
                    }
                }
                .frame(width: 80, height: 80)
                .frame(maxHeight: .infinity)
                .padding(.bottom, 50)
            }
            HStack(alignment: .bottom) {
                VStack(alignment: .leading, spacing: 4) {
                    Text("Following \(name)").font(.system(size: 24, weight: .semibold)).lineLimit(1)
                    Text(photo.map { "Photo by \($0.photographerName)" } ?? "Their wallpaper arrives in a moment.").opacity(0.8)
                }
                .foregroundStyle(.white)
                Spacer()
                Button("Unfollow") { confirmUnfollow = true }.buttonStyle(OnPhotoPillStyle()).frame(height: 40)
            }
            .padding(.horizontal, 24)
            .padding(.vertical, 20)
        }
        .frame(height: 260)
        .clipShape(.rounded(Radius.large))
        .environment(\.colorScheme, .dark)
    }
}

private struct CodeSheet: View {
    @Environment(AppModel.self) private var app
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        @Bindable var friends = app.friends
        VStack(alignment: .leading, spacing: 14) {
            Text("Follow a friend").font(.system(size: 17, weight: .semibold))
            TextField("K7MQ-2XPA", text: $friends.codeText)
                .textFieldStyle(.plain)
                .font(.system(size: 18, design: .monospaced))
                .tracking(3)
                .autocorrectionDisabled()
                .padding(.horizontal, 14)
                .frame(height: 44)
                .background(Color.surface, in: .rounded(Radius.pill))
                .onSubmit { Task { await follow() } }
            if let error = friends.codeError {
                Text(error).font(.system(size: 12)).foregroundStyle(.red)
            }
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }.buttonStyle(PillButtonStyle()).keyboardShortcut(.cancelAction)
                Button {
                    Task { await follow() }
                } label: {
                    HStack(spacing: 6) {
                        if friends.following { ProgressView().controlSize(.mini).tint(Color.paper) }
                        Text("Follow")
                    }
                }
                .buttonStyle(PrimaryButtonStyle())
                .keyboardShortcut(.defaultAction)
                .disabled(!friends.codeIsValid || friends.following)
            }
        }
        .padding(24)
        .frame(width: 380)
        .background(Color.paper)
    }

    private func follow() async {
        if await app.friends.follow() { dismiss() }
    }
}

private struct FollowersSheet: View {
    @Environment(AppModel.self) private var app
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        let friends = app.friends
        VStack(alignment: .leading, spacing: 14) {
            Text("Followers").font(.system(size: 17, weight: .semibold))
            if friends.followers.isEmpty && !friends.loadingFollowers {
                Text("No one yet. Share your wallpaper and friends who follow show up here.").foregroundStyle(Color.muted)
            } else {
                ScrollView {
                    VStack(spacing: 12) {
                        ForEach(friends.followers) { follower in
                            HStack(spacing: 12) {
                                Text(String(follower.name.prefix(1)).uppercased())
                                    .font(.system(size: 14, weight: .semibold))
                                    .foregroundStyle(Color.paper)
                                    .frame(width: 36, height: 36)
                                    .background(Color.ink, in: Circle())
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(follower.name).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                                    if let since = follower.since {
                                        Text("Since \(since.formatted(.dateTime.month(.abbreviated).day()))")
                                            .font(.system(size: 12)).foregroundStyle(Color.muted)
                                    }
                                }
                                Spacer()
                                Button(friends.confirmRemove == follower.uid ? "Remove?" : "Remove") {
                                    Task { await friends.remove(follower) }
                                }
                                .buttonStyle(PillButtonStyle())
                            }
                            .padding(6)
                        }
                    }
                }
                .frame(maxHeight: 320)
            }
            HStack {
                Spacer()
                Button("Done") { dismiss() }.buttonStyle(PrimaryButtonStyle()).keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 420)
        .background(Color.paper)
        .task { await friends.loadFollowers() }
    }
}
