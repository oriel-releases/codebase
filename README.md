# Oriel for macOS

A native macOS app that gives you a fresh, high-quality Unsplash photo as your desktop wallpaper on a schedule, the
same photo on every device of your account. Swift 6, SwiftUI + AppKit, lives in the menu bar. The macOS twin of
Oriel for Windows: [docs/mac-task.md](docs/mac-task.md) is the full specification.

## Requirements

- macOS 26 Tahoe or later
- Xcode 27 (to build) and XcodeGen: `brew install xcodegen`
- An Unsplash access key for Oriel's **macOS** Unsplash app (users never enter one)

## Unsplash key (build time)

The key is embedded in the app's `Info.plist` (`OrielUnsplashAccessKey`) at build time from, in order:

1. the `UNSPLASH_ACCESS_KEY` environment variable (handy for CI), or
2. `UNSPLASH_ACCESS_KEY=...` in `.env` (git-ignored; copy `.env.example`).

`scripts/configure.sh` writes it to `Config/Secrets.xcconfig` (git-ignored). Without a key the app builds but can't fetch photos.

## Build & run

```bash
scripts/configure.sh          # keys -> Config/Secrets.xcconfig, generates Oriel.xcodeproj
scripts/test.sh               # OrielKit unit tests (swift test)
scripts/build.sh              # Debug build -> build/Build/Products/Debug/Oriel.app
scripts/check.sh              # build, printing only errors and warnings
open build/Build/Products/Debug/Oriel.app
build/Build/Products/Debug/Oriel.app/Contents/MacOS/Oriel --status
```

Debug builds are `uz.akbarjon.oriel.dev` (they share the data folder with the installed app).
`ORIEL_ONBOARDING=1` replays the first-run tour.

## Command line

| Flag | What it does |
| --- | --- |
| `--apply-today` | Change the wallpaper if this tick's isn't set yet (launchd runs this) |
| `--change-now` | Change it now (the Change-now limit applies) |
| `--status` | Settings, displays, last run |
| `--background` | Start in the menu bar without the window |

Exit codes: `0` ok, `1` failed (wallpaper kept), `2` bad usage, `3` not signed in or the Unsplash key is missing/rejected.

## Release

1. Once: `build/SourcePackages/artifacts/sparkle/Sparkle/bin/generate_keys` makes the EdDSA key pair. The private key
   stays in this Mac's Keychain (back it up: losing it means installed copies can never update again); put the
   printed public key in `.env` as `SPARKLE_PUBLIC_KEY=`.
2. Bump `MARKETING_VERSION` and `CURRENT_PROJECT_VERSION` in `project.yml`.
3. `GITHUB_TOKEN=… scripts/release.sh` builds, signs ad hoc, makes `Oriel.dmg`, `Oriel.zip` and `appcast.xml`, and
   publishes them to <https://github.com/oriel-releases/macos> as `v<version>` (`--no-upload` builds `releases/` only).
4. Set `version` in the Firestore document `application/macos`; update `fallbackVersions.macos` and `releasedOn` in
   `website/src/config/release.ts` and redeploy the site.

Not notarized (no paid Apple Developer account): on first launch macOS blocks it; the user opens it once from
System Settings → Privacy & Security → Open Anyway.

To work in Xcode, run `scripts/configure.sh` and open `Oriel.xcodeproj`. The project is generated from
`project.yml`, so change that file (not the project in Xcode) and re-run `scripts/configure.sh`.

## Project layout

```
App/Shell            AppKit: app delegate, menu bar, window, links, command line, hotkey, popup, Sparkle
App/Model            the observable models the screens use
App/Views, App/Theme SwiftUI screens and Oriel's design system
OrielKit/            Swift package, no UI: Unsplash, wallpaper pipeline, storage, scheduling, Firebase auth and sync
Config/              xcconfig files (Secrets.xcconfig is generated)
scripts/             configure, build, check, test, release
project.yml          XcodeGen spec for Oriel.xcodeproj
```

## Where things live

| What | Where |
| --- | --- |
| Wallpapers | `~/Pictures/Oriel` |
| Settings, state, account, history | `~/Library/Application Support/Oriel` (`auth.json` is owner-only) |
| Logs | `~/Library/Logs/Oriel/oriel-YYYY-MM-DD.log` |
| Background fallback | `~/Library/LaunchAgents/uz.akbarjon.oriel.apply.plist` (and `.retry`) |

Never change the bundle identifier `uz.akbarjon.oriel` after the first release: updates and saved data depend on it.

Photos are provided by [Unsplash](https://unsplash.com/?utm_source=oriel&utm_medium=referral) and credited to their photographers.
