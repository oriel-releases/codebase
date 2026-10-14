# Oriel for macOS

A native macOS app that gives you a fresh, high-quality Unsplash photo as your desktop wallpaper every day.
Swift 6 + SwiftUI, lives in the menu bar. The macOS twin of Oriel for Windows; see [PLAN.md](PLAN.md) for the roadmap.

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
scripts/configure.sh          # key -> Config/Secrets.xcconfig, generates Oriel.xcodeproj
scripts/test.sh               # OrielKit unit tests (swift test)
scripts/build.sh              # Debug build -> build/Build/Products/Debug/Oriel.app
open build/Build/Products/Debug/Oriel.app
```

To work in Xcode, run `scripts/configure.sh` and open `Oriel.xcodeproj`. The project is generated from
`project.yml`, so change that file (not the project in Xcode) and re-run `scripts/configure.sh`.

## Project layout

```
App/                 SwiftUI app: menu bar, main window, pages (target "Oriel")
OrielKit/            Swift package, no UI: providers, wallpaper pipeline, storage, scheduling (unit-tested)
Config/              xcconfig files (Secrets.xcconfig is generated)
scripts/             configure, build, test (release comes in M5)
project.yml          XcodeGen spec for Oriel.xcodeproj
```

## Where things live

| What | Where |
| --- | --- |
| Wallpapers | `~/Pictures/Oriel` |
| Settings / state / history | `~/Library/Application Support/Oriel` |
| Logs | `~/Library/Application Support/Oriel/logs` |

Never change the bundle identifier `uz.akbarjon.oriel` after the first release: updates and saved data depend on it.

Photos are provided by [Unsplash](https://unsplash.com/?utm_source=oriel&utm_medium=referral) and credited to their photographers.
