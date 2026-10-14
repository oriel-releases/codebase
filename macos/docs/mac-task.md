# Oriel for macOS: the complete build task

> **For Claude Code running on a Mac.** This file is the full specification for building the macOS version of Oriel.
> It describes the existing Windows app (the reference) in complete detail: every screen, every word, every
> animation, every sync rule and every REST call. Build the Mac app so it behaves **exactly** like the Windows app,
> adapted only where macOS works differently (those places are called out with **Mac:**).
>
> If the `windows/` folder is available next to this file, it is the reference implementation: when this document
> and the code disagree, the code wins, and every section names the Windows file(s) to read. If only this file is
> available, it is meant to be enough on its own.

---

## Table of contents

0. [What Oriel is](#0-what-oriel-is)
1. [Ground rules](#1-ground-rules)
2. [Technology choices for the Mac app](#2-technology-choices-for-the-mac-app)
3. [Project layout, build and run](#3-project-layout-build-and-run)
4. [Files on disk](#4-files-on-disk)
5. [Data models](#5-data-models)
6. [Settings and state](#6-settings-and-state)
7. [History database (SQLite)](#7-history-database-sqlite)
8. [Unsplash provider](#8-unsplash-provider)
9. [Choosing a photo: topics, filters, mood](#9-choosing-a-photo-topics-filters-mood)
10. [The wallpaper service (the heart)](#10-the-wallpaper-service-the-heart)
11. [Prefetch: the next wallpapers, ahead of time](#11-prefetch-the-next-wallpapers-ahead-of-time)
12. [Setting the wallpaper on macOS](#12-setting-the-wallpaper-on-macos)
13. [The schedule: one clock for every device](#13-the-schedule-one-clock-for-every-device)
14. [The in-app scheduler](#14-the-in-app-scheduler)
15. [Background fallback (launchd) and login item](#15-background-fallback-launchd-and-login-item)
16. [Accounts and sign-in](#16-accounts-and-sign-in)
17. [Firebase over REST: every call](#17-firebase-over-rest-every-call)
18. [Cross-device sync](#18-cross-device-sync)
19. [Friends: follow and share](#19-friends-follow-and-share)
20. [Wallpaper of the day](#20-wallpaper-of-the-day)
21. [Anonymous daily stats](#21-anonymous-daily-stats)
22. [Retention and the wallpaper folder](#22-retention-and-the-wallpaper-folder)
23. [Deep links (oriel://)](#23-deep-links-oriel)
24. [App lifecycle: menu bar app, window, single instance](#24-app-lifecycle-menu-bar-app-window-single-instance)
25. [Design system](#25-design-system)
26. [Motion: every animation](#26-motion-every-animation)
27. [Main window and navigation](#27-main-window-and-navigation)
28. [Sign-in screen](#28-sign-in-screen)
29. [Onboarding tour (first run)](#29-onboarding-tour-first-run)
30. [Home](#30-home)
31. [Search](#31-search)
32. [Interests](#32-interests)
33. [History and Favorites](#33-history-and-favorites)
34. [Friends](#34-friends)
35. [Settings](#35-settings)
36. [Shared photo card](#36-shared-photo-card)
37. [Menu bar icon](#37-menu-bar-icon)
38. [Global shortcut](#38-global-shortcut)
39. [The change popup (the pill)](#39-the-change-popup-the-pill)
40. [Progress words (phases)](#40-progress-words-phases)
41. [Command line](#41-command-line)
42. [Updates (Sparkle)](#42-updates-sparkle)
43. [Logging](#43-logging)
44. [Performance and battery](#44-performance-and-battery)
45. [Packaging and releasing](#45-packaging-and-releasing)
46. [Backend changes Akbar must make for macOS](#46-backend-changes-akbar-must-make-for-macos)
47. [Build order (phases) with checks](#47-build-order-phases-with-checks)
48. [Tests to port](#48-tests-to-port)
49. [Final acceptance checklist](#49-final-acceptance-checklist)
50. [Appendix A: every user-facing string](#appendix-a-every-user-facing-string)
51. [Appendix B: built-in data (interests, covers, emojis, search ideas)](#appendix-b-built-in-data)
52. [Appendix C: icon map (Windows glyph → SF Symbol)](#appendix-c-icon-map)
53. [Appendix D: Windows → macOS mapping table](#appendix-d-windows--macos-mapping-table)

---

## 0. What Oriel is

Oriel is a free wallpaper app: a fresh Unsplash photo on a schedule, **the same photo on every device of an
account** (Windows, Linux, Android, the browser extension, and now macOS).

| Part | Folder | Stack | Released to |
|---|---|---|---|
| Windows app (the reference) | `windows/` | C#, WinUI 3, Velopack | `github.com/oriel-releases/windows` |
| Linux app | `linux/` | C#, Avalonia, Velopack AppImage | `github.com/oriel-releases/linux` |
| Android app | `android/` | Kotlin, Jetpack Compose | `github.com/oriel-releases/android` |
| Browser extension | `extension/` | TypeScript, Vite | Chrome / Edge / Firefox stores |
| Website | `website/` | Next.js static export, Firebase Hosting | https://oriel.akbarjon.uz |
| **macOS app (this task)** | **`macos/`** | **Swift, SwiftUI + AppKit, Sparkle** | **`github.com/oriel-releases/macos`** |

Backend: the Firebase project **`oriel-gcp`** only (Auth, Firestore, Realtime Database, Hosting). **Hard
constraint: everything stays free.** Spark plan, no Blaze, no credit card, no Cloud Functions, no paid services.
That also means **no paid Apple Developer account is assumed** (see §45).

What the app does, in one breath: you sign in (Google or email, through the website), pick interests, and Oriel
sets a new Unsplash photo as your desktop wallpaper once a day (or every 1/3/6/12 hours). It never repeats a photo,
keeps a History, lets you star Favorites (with a TikTok-style star burst when you double-click the photo), search
Unsplash and set any result, share a photo with a link, let friends follow your wallpaper, and keeps everything
(current photo, schedule, interests, favorites) in sync with your other devices within a second. It lives in the
menu bar and uses almost no memory or battery between changes.

---

## 1. Ground rules

1. **Same behavior as Windows.** Same features, same words (Appendix A), same numbers (sizes, timings, limits), same
   sync protocol. Where macOS can't do something (accent color, Windows-style lock screen API, tile fit), drop it
   as described here; don't invent replacements beyond what this document says.
2. **Native Mac.** It must look and feel like a real Mac app: SwiftUI/AppKit controls, SF Pro, SF Symbols, system
   light/dark, keyboard navigation, VoiceOver labels. But it uses **Oriel's own black-and-white palette** (§25), not
   the system blue accent.
3. **Free and light.** No Firebase SDK (everything is REST, like the desktop apps). Only one third-party dependency:
   **Sparkle** (updates). Keep Firestore traffic to single small documents (never list whole collections except the
   ones listed in §17). Idle = nothing running but timers and one SSE connection.
4. **Never break other platforms.** You only add `macos/`. The shared backend contract (document shapes, field
   names, RTDB doorbell, slot IDs, schedule math) must stay byte-for-byte compatible with Windows, Linux, Android and
   the extension. Backend rule changes are listed in §46 for Akbar (the owner) to deploy; don't deploy anything.
5. **Respect Unsplash's terms:** hotlink Unsplash image URLs, ping the download-tracking endpoint whenever a photo is
   used as a wallpaper, always credit "Photo by {name} on Unsplash" with links (UTM parameters, §8).
6. **Never log secrets** (tokens, refresh tokens, the Unsplash key).
7. **Git workflow:** never push to `main`. Work on a branch (e.g. `macos-1.0.0`), split work into meaningful commits,
   open a pull request into `main`. Commit messages and code comments in the repo's style: short, plain English,
   explaining *why*.
8. **Copy style:** plain, short, friendly sentences. Use the typographic ellipsis `…` (one character) in progress
   words. Use `·` as a separator in meta lines. Use `✓` where Windows does.
9. **Comment style:** each type and non-obvious function gets a one or two sentence doc comment in plain English, like
   the Windows code (`/// <summary>`… becomes `///` in Swift).

---

## 2. Technology choices for the Mac app

| Concern | Choice | Why |
|---|---|---|
| Language / UI | **Swift 5.10+ (Swift 6 OK), SwiftUI for screens, AppKit where SwiftUI falls short** (status item, the popup panel, window management, share picker) | Native look; the Windows app was explicitly built to feel native |
| Minimum macOS | **13.0 Ventura** | `SMAppService` (login item) and `MenuBarExtra`/modern SwiftUI need 13. Use `ObservableObject` + `@Published` (not `@Observable`, which needs 14) |
| Architecture | `OrielCore` (pure logic, no UI, unit-tested) + `Oriel` app target (UI, platform glue). Mirror the Windows `Oriel.Core` / `Oriel.App` split and class names | Easy to compare against Windows; "change one, change the other" |
| Concurrency | Swift `async/await`, actors for services with shared state (a `SemaphoreSlim(1,1)` in C# becomes an actor or an async lock) | |
| HTTP | `URLSession` (shared, `timeoutIntervalForRequest = 60`, User-Agent `Oriel/1.0 (+desktop wallpaper app)`) | Happy-eyeballs IPv4/IPv6 racing is built into Apple's networking, so the Windows `FastConnect` class is **not** needed |
| SSE (live sync) | `URLSession.bytes(for:)` + `.lines` | Plain HTTP, no SDK |
| SQLite | System `SQLite3` C API via a tiny wrapper (no GRDB) | No dependency |
| JSON | `Codable` | |
| Images | `CGImageSource` (thumbnail decode at a max pixel size), `NSImage` | Never decode full-size photos for the UI |
| Updates | **Sparkle 2** (Swift Package) | Free, standard |
| Global hotkey | Carbon `RegisterEventHotKey` | Works without Accessibility permission |
| Login item | `SMAppService.mainApp` | |
| Background fallback | A launchd user agent plist in `~/Library/LaunchAgents` (see §15) | Same role as Windows Task Scheduler |
| Project generation | **XcodeGen** (`brew install xcodegen`), `project.yml` checked in, `.xcodeproj` git-ignored; build with `xcodebuild` | Claude works from the terminal; a YAML project is reviewable |
| Tests | XCTest target `OrielCoreTests` | Port the Windows xUnit tests (§48) |
| Bundle ID | Release: **`uz.akbarjon.oriel`** (same as Android). Debug: **`uz.akbarjon.oriel.dev`** | Separate data for dev builds is not needed; same data folder is fine |
| App name | **Oriel**. Window title **"Oriel (Beta)"** while in beta | Matches Windows |
| Architectures | Universal (arm64 + x86_64) | |
| Sandbox | **Not sandboxed** (distributed outside the App Store; it writes `~/Library/LaunchAgents`, reads/writes a user-chosen folder, sets the wallpaper) | |

---

## 3. Project layout, build and run

```
macos/
  project.yml                 XcodeGen project (targets: Oriel app, OrielCore framework, OrielCoreTests)
  README.md                   what it is, build/run/release (like windows/README.md)
  .env                        UNSPLASH_ACCESS_KEY=... (git-ignored)
  .gitignore                  *.xcodeproj, build/, releases/, .env, Secrets.generated.swift
  Config/
    Oriel.xcconfig            versions, bundle ids
    Info.plist                LSUIElement NO, CFBundleURLTypes (oriel), SUFeedURL, SUPublicEDKey, LSMinimumSystemVersion 13.0
  Sources/
    OrielCore/
      AppPaths.swift  OrielLinks.swift
      Models/         Models.swift  ShareLink.swift
      Settings/       OrielSettings.swift  JsonStore.swift
      Storage/        HistoryStore.swift  (SQLite)
      Providers/      ImageProvider.swift  UnsplashProvider.swift
      Services/       WallpaperService.swift  CandidateFilter.swift  PhotoMood.swift  RetentionService.swift
                      WallpaperFolderMover.swift  SyncPublisher.swift  SettingsSync.swift  FavoritesSync.swift
                      PulseListener.swift  FollowService.swift  ShareService.swift  TodayService.swift
                      StatsReporter.swift  Clock.swift  Phases.swift
      Scheduling/     ScheduleRules.swift  DailyScheduler.swift  ScheduledRun.swift  ScheduleSync.swift  LaunchAgents.swift
      Auth/           FirebaseClient.swift  AuthService.swift  Account.swift  AccountStore.swift  DeepLink.swift
      Wallpaper/      WallpaperSetter.swift (protocol + MacWallpaperSetter)  AccentPicker.swift  AccentSampler.swift  SystemTheme.swift
      Logging/        FileLogger.swift
      Security/       ApiKeyStore.swift  (reads the build-time key)
    Oriel/
      OrielApp.swift  AppDelegate.swift  AppHost.swift (composition root)  Cli.swift
      MainWindow/     MainWindowController.swift  RootView.swift  Sidebar.swift
      Pages/          HomePage.swift  SearchPage.swift  InterestsPage.swift  HistoryPage.swift  FriendsPage.swift  SettingsPage.swift
      Overlays/       SignInView.swift  OnboardingView.swift  SharedPhotoView.swift  SigningOutView.swift
      ViewModels/     HomeViewModel.swift  CollectionsViewModel.swift  HistoryViewModel.swift  FriendsViewModel.swift
                      SettingsViewModel.swift  OnboardingViewModel.swift  SignInViewModel.swift  SharedPhotoViewModel.swift
                      SearchIdeas.swift  InterestCovers.swift
      Controls/       SettingRow.swift  InfoBar.swift  PillButtonStyles.swift  PhotoTile.swift  ImageReveal.swift  StarBurst.swift
                      SegmentedPill.swift  TimeField.swift  ShortcutRecorder.swift
      Services/       StatusItemController.swift  HotkeyService.swift  ChangePopup.swift  UpdateService.swift
                      ShareSheet.swift  ImageLoader.swift  LoginItem.swift
      Theme/          Colors.swift  Typography.swift  Radii.swift
      Assets.xcassets AppIcon, Logo (light), LogoDark, GoogleG, MenuBarIcon (template)
  Tests/OrielCoreTests/ ...
  scripts/
    build.sh  release.sh  make-dmg.sh
```

**Unsplash key at build time** (users never enter one): read `UNSPLASH_ACCESS_KEY` from the environment, else from
`macos/.env` (`UNSPLASH_ACCESS_KEY=...`). A pre-build script phase writes `Sources/OrielCore/Security/Secrets.generated.swift`
(git-ignored) with `enum Secrets { static let unsplashAccessKey = "…" }` (empty string when missing, and print a
build warning: "No Unsplash key: set UNSPLASH_ACCESS_KEY or add it to macos/.env. The app will build but can't fetch
photos."). Ask Akbar for the production key; for development you can make your own free demo key at
https://unsplash.com/developers (Access Key, not Secret key). The key is shared by every install and rate limited
(demo apps: 50 requests/hour; one change costs about 2: one search + one download-tracking ping).

Build and run:

```bash
cd macos
xcodegen generate
xcodebuild -scheme Oriel -configuration Debug -derivedDataPath build build
open build/Build/Products/Debug/Oriel.app
xcodebuild -scheme OrielCoreTests test -derivedDataPath build
build/Build/Products/Debug/Oriel.app/Contents/MacOS/Oriel --status
```

Dev helper env var (same as Windows): `ORIEL_ONBOARDING=1` replays the onboarding tour even when History isn't empty.

---

## 4. Files on disk

Windows → Mac:

| What | Windows | **Mac** |
|---|---|---|
| Data root | `%LOCALAPPDATA%\Oriel` | `~/Library/Application Support/Oriel` |
| Settings | `settings.json` | same name, in data root |
| Runtime state | `state.json` | same |
| Account (refresh token) | `auth.bin` (DPAPI-encrypted) | `auth.json` with POSIX mode **0600** (see §16 for why not the Keychain) |
| Stats memory | `stats.json` | same |
| History DB | `oriel.db` | same |
| Prefetched photos | `next/` (+ `next/queue.json`) | same |
| Cross-process lock | `apply.lock` | same (use `flock(LOCK_EX | LOCK_NB)`) |
| Wallpaper of the day list cache | `today.json` | same |
| Logs | `%LOCALAPPDATA%\Oriel\logs\oriel-YYYY-MM-DD.log` | `~/Library/Logs/Oriel/oriel-YYYY-MM-DD.log` |
| Wallpapers | `Pictures\Oriel` (or a folder the user picked) | `~/Pictures/Oriel` (or a folder the user picked) |

Create the data root and logs folder at startup. Every JSON write is atomic: write `file.tmp`, then rename over the
target. A corrupt or unreadable JSON file silently falls back to defaults (never crash).

Wallpaper file names: `{yyyy-MM-dd}_{provider}_{sanitizedPhotoId}{_index if index>0}.jpg`, e.g.
`2026-10-14_unsplash_FIKD9t5_5zQ.jpg`. `sanitize` keeps only letters, digits, `-` and `_`. Downloads are written to
`name.jpg.part` first, then renamed. Date in local time.

---

## 5. Data models

Reference: `windows/src/Oriel.Core/Models/Models.cs`, `ShareLink.cs`.

```swift
/// A photo offered by an image provider, before it is downloaded.
struct PhotoCandidate: Codable, Equatable {
    var provider: String          // "unsplash"
    var id: String
    var width: Int
    var height: Int
    var likes: Int = 0
    var description: String?      // alt_description and description joined with " — " (distinct, non-empty)
    var color: String?            // "#RRGGBB" from Unsplash
    var rawUrl: String            // urls.raw (accepts sizing params)
    var thumbnailUrl: String      // urls.small, else urls.thumb
    var photoPageUrl: String      // links.html + UTM
    var photographerName: String  // user.name, else user.username, else "Unknown"
    var photographerUrl: String   // user.links.html + UTM
    var downloadTrackingUrl: String? // links.download_location
    var isLandscape: Bool { width > height }
}

enum WallpaperFit: String, Codable { case fill, fit, stretch, center, span }   // Mac: no "tile" (see §12)

struct MonitorInfo { var id: String; var left, top, width, height: Int  /* pixels */ ; var isPrimary: Bool }
struct PixelSize: Codable, Equatable { var width: Int; var height: Int }

/// A wallpaper that Oriel has applied at some point.
struct HistoryEntry {
    var id: Int64 = 0
    var provider: String
    var photoId: String
    var filePath: String          // "" for a favorite synced from another device that was never downloaded here
    var thumbnailUrl: String
    var photoPageUrl: String
    var photographerName: String
    var photographerUrl: String
    var description: String?
    var topic: String?            // the interest it came from, or "search query", "search", "favorite", "shared",
                                  // "synced", "from {FriendName}", "wallpaper of the day"
    var monitorPath: String?      // the display it was set on with "a photo for each display"; nil = all
    var appliedAt: Date
    var isFavorite: Bool = false
    var isDeleted: Bool = false
}
```

**SharedPhoto / ShareLink helpers** (used by share links, follow pages and sync):

- `SharedPhoto(id, path, photographer, handle)`.
- `ShareLink.forShare(id)` = `https://oriel.akbarjon.uz/share/{id}`; `ShareLink.forFollow(code)` = `https://oriel.akbarjon.uz/follow/{code}`.
- `ShareLink.photo(entry)` → `SharedPhoto?`: only if `entry.thumbnailUrl` is an absolute URL whose host is exactly
  `images.unsplash.com`. `path` = the URL path without the leading `/`; must be 6–120 chars of `[A-Za-z0-9_-]`, else nil.
  `handle` = from `entry.photographerUrl` when its host is `unsplash.com` and its path is `/@handle` with handle 1–40
  chars of `[A-Za-z0-9_.-]`, else `""`. `photographer` = name truncated to 80 chars.
- `ShareLink.picture(url)` → the URL only if ≤400 chars, `https`, host `lh3.googleusercontent.com`; else `""`.
- `ShareLink.cleanName(name)` → first word of the trimmed name, without control chars and `<` `>`, max 30 chars; `""` if blank.

---

## 6. Settings and state

Reference: `windows/src/Oriel.Core/Settings/OrielSettings.cs`, `JsonStore.cs`.

`settings.json` (camelCase keys, enums as strings). Defaults in brackets:

| Field | Type [default] | Synced? | Meaning |
|---|---|---|---|
| `topics` | `[TopicSetting]` [built-ins; `nature, mountains, ocean, space, architecture` on] | yes (as `interests`) | Interests. `TopicSetting {query, enabled, isCustom, coverUrl?}`. `coverUrl` is device-only (custom interests' tile photo) |
| `dailyTime` | `"HH:mm"` ["09:00"] | yes | Time of the daily change, in `timeZone` |
| `timeZone` | IANA id [this Mac's `TimeZone.current.identifier`] | yes | The zone the schedule is anchored in (shared by every device) |
| `intervalMinutes` | Int [0] | yes | 0 = once a day; else 60/180/360/720 (slideshow) |
| `favoritesOnly` | Bool [false] | yes | "Pick from: My favorites" |
| `applyAtLogin` | Bool [true] | no | "Change at login" (catch up a missed change at startup) |
| `paused` | Bool [false] | yes | Lock: stops only the schedule |
| `fit` | WallpaperFit [fill] | no | |
| `skipLowLikes` | Bool [false] | yes | |
| `minLikes` | Int [50] | yes | slider 0…1000 step 50 |
| `wallpaperFolder` | String? [nil = ~/Pictures/Oriel] | no | |
| `startAtLogin` | Bool [true] | no | Windows `StartWithWindows` |
| `matchTimeOfDay` | Bool [true] | yes | Prefer dark photos in the evening, bright in the morning |
| `photoPerMonitor` | Bool [false] | no | A photo for each display |
| `followUid`, `followName`, `followPicture` | String? | no | Following a friend (per device) |
| `myFollowCode` | String? | no | This device's code while sharing |
| `onboardingDone` | Bool [false] | no | |
| `hotkey` | String ["Ctrl+Alt+W"] | no | Stored with Windows-style names (`Ctrl`, `Alt`=Option, `Shift`, `Cmd`); shown as `⌃⌥W` |

Windows-only settings **not on Mac**: `changeLockScreen`, `matchAccentColor` (see §12). Keep decoding tolerant of
unknown keys.

`isFollowing` = `followUid` non-empty. `interval` = `intervalMinutes > 0 ? minutes : nil`.
`getDailyTime()` parses `"HH:mm"`, falls back to 09:00. `zone()` = `TimeZone(identifier: timeZone) ?? .current`.

**Built-in interests** (Appendix B has the list of 24). `updateBuiltInTopics()` runs on every load:
- the result lists every built-in (in the built-in order) with the saved on/off (off if new),
- then every custom one kept as is,
- a saved non-custom topic that is no longer built in is kept **as a custom one only if it was on**,
- if nothing was saved yet (brand-new install), keep the defaults.

`enabledQueries()` = enabled, non-blank, trimmed, distinct (case-insensitive).

**Store:** `load()`, `save()` (fires `settingsChanged`), `loadState()`, `saveState()`, and an `update { s in … }`
helper that does load-modify-save (so different screens never overwrite each other's changes). Guard reads/writes
with a lock.

**`state.json` (`OrielState`)**, not user-editable:

| Field | Meaning |
|---|---|
| `lastAppliedDate`, `lastAppliedAt` | Last **scheduled** (or chosen) change |
| `lastAttemptAt` | Last scheduled attempt |
| `lastError`, `consecutiveFailures`, `nextRetryAt` | Failure + backoff |
| `manualChanges: [Date]` | Successful "Change now"s (only the last hour matters) |
| `scheduleStartedAt` | When the schedule last started fresh (an edit here or elsewhere); ticks before it aren't "missed" |
| `syncedSettingsHash`, `settingsSyncedAt`, `settingsDirty` | Settings sync bookkeeping (§18) |
| `favoritesSyncedAt` | Newest favorite change taken from the account |
| `followedPhotoId` | The account's / friend's photo already applied here |
| `followProblem` | "X stopped sharing their wallpaper." for the UI |

---

## 7. History database (SQLite)

Reference: `windows/src/Oriel.Core/Storage/SqliteHistoryStore.cs`.

Open a new connection per operation (no pooling, so the CLI run and the app can both use the file), set
`PRAGMA busy_timeout = 5000`. On first open:

```sql
PRAGMA journal_mode = WAL;
CREATE TABLE IF NOT EXISTS history (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    provider          TEXT NOT NULL,
    photo_id          TEXT NOT NULL,
    file_path         TEXT NOT NULL,
    thumbnail_url     TEXT NOT NULL,
    photo_page_url    TEXT NOT NULL,
    photographer_name TEXT NOT NULL,
    photographer_url  TEXT NOT NULL,
    description       TEXT,
    topic             TEXT,
    monitor_path      TEXT,
    applied_at        TEXT NOT NULL,      -- ISO 8601 UTC with fraction ("O" format), sorts as text
    is_favorite       INTEGER NOT NULL DEFAULT 0,
    is_deleted        INTEGER NOT NULL DEFAULT 0,
    UNIQUE (provider, photo_id)
);
CREATE INDEX IF NOT EXISTS ix_history_applied ON history (applied_at);
```

Operations:
- `wasUsed(provider, photoId)` → any row (deleted ones too). **This is "never repeat a photo".**
- `add(entry) -> id`: `INSERT … ON CONFLICT (provider, photo_id) DO UPDATE SET file_path = excluded.file_path, topic = excluded.topic, applied_at = excluded.applied_at, is_deleted = 0;` then `SELECT id`. (Re-applying a photo moves it to the top of History.)
- `list(includeDeleted = false)` ordered `applied_at DESC, id DESC`.
- `get(id)`.
- `setFavorite(id, bool)` then fire `favoriteChanged(entry)` (FavoritesSync listens).
- `markDeleted(id)` (soft delete: the row stays so the photo never repeats).
- `expiredBefore(cutoff)`: `is_favorite = 0 AND is_deleted = 0 AND applied_at < cutoff`.
- `updateFilePaths([old: new])` in one transaction (case-insensitive match on old path).

**Home's "current wallpaper" is always `list().first`** (the newest non-deleted entry).

---

## 8. Unsplash provider

Reference: `windows/src/Oriel.Core/Providers/UnsplashProvider.cs`, `IImageProvider.cs`.

- API base `https://api.unsplash.com/`. Headers: `Authorization: Client-ID {key}`, `Accept-Version: v1`.
- UTM suffix added to photo page and photographer links: `utm_source=oriel&utm_medium=referral` (with `?` or `&`).
- **Search:** `GET search/photos?orientation=landscape&content_filter=high&order_by=relevant&per_page=30&page={max(1,page)}&query={escaped}`.
  Returns `SearchPage(photos, totalPages)` (`total_pages`, default 1).
- **Random for a topic** (`getRandom(query, size)`): pick a random page 1…3, search it; if empty and page > 1, search
  page 1; if still empty throw `.noResults("No photos found for that topic.")`; return the results **shuffled**.
  (Relevance first, not Unsplash's loose `/photos/random`.)
- **One photo:** `GET photos/{id}` (shared links, synced photos, wallpaper of the day).
- **Download:** the image CDN, no key, doesn't count against the limit:
  `{raw}{?|&}w={W}&h={H}&fit=crop&crop=entropy&q=90&fm=jpg`. Timeout 25 s per try, **one retry**, then
  `.network("The photo is taking too long to download. Check your connection and try again.")` (timeout) or
  `.network("Couldn't download the photo. Check your connection and try again.")`.
  A download is valid only if it's > 1024 bytes and starts with `FF D8` (JPEG); otherwise throw
  "Downloaded file isn't a valid image; keeping your current wallpaper."
- **Track usage:** `GET {download_location}` with the key, after a photo is set. Failures are only logged.
- **Rate limit:** read `X-Ratelimit-Remaining` from every API response into `remainingRequests`. If it reaches 0, or
  on HTTP 429, or 403 while remaining is 0: mark rate-limited **until the top of the next UTC hour** and throw
  `.rateLimited("Unsplash hourly limit reached. Will try again later.")`. While rate-limited, API calls throw that
  without hitting the network.
- Errors: 401 → `.unauthorized("Unsplash rejected Oriel's access key.")`; other 403 → `.unauthorized("Unsplash refused the request (403).")`;
  404 → `.noResults("That photo isn't available (any more).")`; other → `.badResponse("Unsplash returned HTTP {code}.")`;
  transport error → `.network("Can't reach Unsplash. Are you offline?")`; timeout → `.network("Unsplash took too long to answer.")`;
  unparsable JSON → `.badResponse("Unsplash returned data Oriel couldn't read.")`; no key →
  `.missingKey("This build of Oriel has no Unsplash access key.")`.
- `ProviderErrorKind`: `missingKey, unauthorized, rateLimited, noResults, network, badResponse`. Transient (retry with
  backoff) = `network, rateLimited, badResponse` (and unknown errors).

---

## 9. Choosing a photo: topics, filters, mood

References: `CandidateFilter.cs`, `PhotoMood.cs`.

**Topic picker:** random among enabled queries, avoiding the ones in `avoid` when there is a choice. With no enabled
interests the fallback topic is **`"landscape"`**.

**Candidate filter** (`reject(candidate, target, settings)` returns a reason or nil):
1. not landscape → "portrait"
2. width < target.width or height < target.height → "too small"
3. `skipLowLikes && likes < minLikes` → "low likes"

**Mood** (only when `matchTimeOfDay` is on, else `.any`):
- `.dark` when local hour ≥ 19 or < 6, **or the system is in dark mode** (Mac: `NSApp.effectiveAppearance` best-match
  is `.darkAqua`, or `UserDefaults.standard.string(forKey: "AppleInterfaceStyle") == "Dark"` from a non-UI process);
- `.bright` when hour < 11; otherwise `.any`.
- Luminance of the Unsplash `color` "#RRGGBB": sRGB → linear (`c/255`, `≤0.03928 ? c/12.92 : ((c+0.055)/1.055)^2.4`),
  `L = 0.2126 R + 0.7152 G + 0.0722 B`.
- Fits: dark → `L < 0.18`; bright → `L > 0.3`; a photo with no color always fits.
- `order(photos, mood)`: the fitting ones first (original order), then the rest. It's a preference, never a filter.

**Finding a candidate** (`findCandidate(size, enabled, usedTopics, pickedIds, settings, mood)`): up to **3 topics**
(`maxQueriesPerTarget`). For each: pick a topic not tried yet, `getRandom`; on `.noResults` remember it and try the
next topic. Walk the mood-ordered candidates and return the first that is not already picked in this change, not
`history.wasUsed`, and passes the filter. If nothing: rethrow the last no-results error, or
`.noResults("Couldn't find a new photo that passes your filters. Try more topics or looser filters.")`.

---

## 10. The wallpaper service (the heart)

Reference: `windows/src/Oriel.Core/Services/WallpaperService.cs` (read it fully if available).

### 10.1 Results

```swift
enum ApplyStatus { case applied, alreadyAppliedToday, paused, failed, busy, signInRequired, rateLimited, takenByOtherDevice }
struct ApplyResult { var status: ApplyStatus; var entries: [HistoryEntry]; var error: String? = nil; var errorKind: ProviderErrorKind? = nil
    var isTransientFailure: Bool { status == .failed && (errorKind == nil || [.network, .rateLimited, .badResponse].contains(errorKind!)) } }
```

Events: `applied(ApplyResult)` after any successful change in this process; `phaseChanged(ApplyPhase)` (§40) from
whatever thread is working.

### 10.2 `apply(force:)` — scheduled change (force = false) or "Change now" (force = true)

In order:
1. Not signed in → `.signInRequired` ("Sign in to Oriel first.").
2. Load settings and state.
3. `!force && paused` → `.paused`. `!force && isFollowing` → `.paused` (the friend's wallpaper is used instead).
4. `!force && ScheduleRules.isFresh(...)` → `.alreadyAppliedToday`.
5. Take the **cross-process lock** (`apply.lock`, non-blocking). Not available → `.busy("Another change is already in progress.")`.
6. Reload state; re-check `isFresh` (another process may have just finished).
7. **First device wins** (only when `!force`): `slot = ScheduleRules.slotId(settings, ScheduleRules.dueTick(settings, now))`;
   `claimer.tryClaim(slot)`; if it returns **false** (another device took it): set `state.scheduleStartedAt = now`,
   `state.nextRetryAt = nil`, save, return `.takenByOtherDevice`. (`nil` = couldn't tell, offline → go ahead.)
8. If force: drop `manualChanges` older than an hour; if `manualChangeAvailableAt(state, now)` is non-nil →
   `.rateLimited("You can use Change now twice an hour. Try again in {N} min.")` where
   `N = max(1, ceil(minutes until))`. **Limit: 2 successful Change-nows per rolling hour.** Available again at
   `recent[count-2] + 1h` (the second-newest of the recent ones).
9. If !force: `state.lastAttemptAt = now`.
10. `entries = applyCore(settings)` (below). On success:
    - force: append `now` to `manualChanges` (manual changes never move the schedule);
    - !force: `lastAppliedDate = today`, `lastAppliedAt = now`, clear `lastError`, `consecutiveFailures = 0`, `nextRetryAt = nil`.
    - save state; **publish** the first entry to the account (`publishSoon`, §10.6); fire `applied`; schedule prefetch (§11).
11. On failure: message = the provider/wallpaper error's text, else "Something went wrong: {message}". Log it. If
    !force: `lastError = message`, `consecutiveFailures += 1`, `nextRetryAt = now + backoff(consecutiveFailures)`
    with backoff steps **2 min, 5 min, 15 min, 30 min, 1 h** (stays at 1 h). Return `.failed(message, kind)`.
    A failed Change now never delays the schedule.
12. Always end with `finishPhase()` (sends `.done` unless a publish is still running).

### 10.3 `applyCore(settings)`

1. Phase `.finding`.
2. If `favoritesOnly`: pick a random favorite **with a file on disk** other than the current wallpaper (if more than
   one); re-apply it (no download, no Unsplash request); add a history row with `appliedAt = now`; return it. If there
   are no favorites with files, fall through to new photos.
3. No key → `.missingKey`.
4. `targets = planTargets(monitors, settings)` (below). `enabled = settings.enabledQueries()`. `mood = moodNow`.
5. For each target: if exactly one target and a **prefetched photo still fits** (§11), use it (no search, no
   download). Otherwise `findCandidate` (avoiding topics/ids used for earlier targets in this change), phase
   `.downloading`, download at the target size, validate JPEG, save into the wallpaper folder.
   **Everything is downloaded before the desktop is touched**, so a failure halfway never leaves a half-changed desktop.
6. Phase `.changing`. Set: one target → `setForAll(file, fit)`; several (photo per display) → `setForMonitor` each.
   Errors become "macOS refused to set the wallpaper: {message}" (Windows says "Windows refused…").
7. After-desktop extras (Windows: lock screen + accent color) — **Mac: none** (see §12).
8. Record one history entry per target (`topic` = the interest, `monitorPath` for per-display), then
   `trackUsage` for each (in the background, §10.6).

### 10.4 `planTargets(monitors, settings, perMonitor = true)`

- No monitors → one target 1920×1080.
- `fit == .span` → one target, the bounding box of all displays.
- `perMonitor && photoPerMonitor && count > 1` → one target per display, sized for it.
- Otherwise one target sized for the **biggest** display (so nothing is upscaled).

**Mac sizes:** use each `NSScreen`'s **pixel** size = `frame.size × backingScaleFactor`. **Cap the target width at
3840 px** (keep the display's aspect ratio for the height). Reason: a 5K/6K Retina display would otherwise reject
most Unsplash photos as "too small" and every download would be enormous. Document this in code.

### 10.5 Other entry points

- `search(query, page)`: no key → missingKey. If `remainingRequests < 10` (SearchReserve) → `.rateLimited("Searching is paused for a while so your daily wallpaper still gets its photos. Try again in the next hour.")`.
  Returns only landscape photos at least as big as the (non-per-monitor) target.
- `findCover(query) -> String?`: one search (page 1), only if remaining ≥ 10; returns the first landscape result's
  thumbnail URL (else the first result's). For custom interest tiles.
- `getSharedPhoto(id)` → `provider.getPhoto(id)`.
- `applyPhoto(photo, topic, publish = true)`: set one specific photo (Search result, shared link, wallpaper of the
  day, a photo from another device). Sign-in check, lock (busy), target = non-per-monitor size, phase
  `.downloading`, download, validate, save (index 0), phase `.changing`, `setForAll`, record entry
  (`topic ?? "search"`), track usage, **counts as the scheduled change** (lastAppliedDate/At = now, clear error and
  retry), publish if `publish`, fire `applied`. Failures return `.failed` (never touch schedule state).
- `reapply(entry)` (History "Set again", favorites rotation): if the file is gone and provider is unsplash → fetch the
  photo by id and `applyPhoto(photo, topic: entry.topic ?? "favorite")` (throws "Couldn't set it. Try again." on
  failure). Else phase `.changing`, set it again (per-display entry on its display when that display still exists,
  else all), add a history row with `appliedAt = now` (moves to top), **publish** it, fire `applied`.
  File missing and not Unsplash → "That image file is gone from disk."
- `reportPhase(phase)` for FollowService ("Receiving…").

### 10.6 After-change work and publishing

When the app is running (`prefetchEnabled = true`, i.e. a long-lived process), network work that doesn't change the
screen runs **after** the change, in a serial background queue, never failing the change: download tracking.
In a one-shot CLI run it's awaited inline (the process is about to exit).

**Publishing** (`publishSoon(entry)`): has its **own** serial queue (so devices see changes in order and it's not
stuck behind slower work). Increment a `publishing` counter, emit phase `.syncing`, run `syncPublisher.publish(entry)`
(never throws to the caller), then decrement; when it reaches 0 emit `.done`. `finishPhase()` emits `.done` only when
`publishing == 0`. So the UI shows "Syncing…" until the account has the new photo.

---

## 11. Prefetch: the next wallpapers, ahead of time

Reference: `WallpaperService.cs` ("the next wallpapers, ahead of time").

Only in the running app (`prefetchEnabled`). Goal: Change now and scheduled changes are instant and work offline.

- Depth **2** (= the Change-now limit per hour).
- Skip when: not signed in, `favoritesOnly`, following, no key, or more than one target (per-display).
- Before filling, delete stray files in `next/` that aren't in `queue.json` (half-written ones).
- Loop while queue < 2 **and** `remainingRequests` is unknown or ≥ **20** (2 × SearchReserve): the first entry should
  suit **now** (mood at now), the second the **next scheduled change** (`now + interval`, or the next daily time);
  use different topics/photos than what's already queued. Download, validate, write `next/{id}.jpg.part` → rename,
  then append to the queue **only once the file is whole** (`{photo, topic, width, height, file, at}`).
- Started 10 s after every successful change, and once at app start.
- Only one filler at a time; a separate lock guards the queue file so taking a photo never waits for a download.
- **Taking** (`takePrefetch(size, enabled, settings, mood)`): keep entries whose file exists, same width/height, younger
  than **3 days**, whose topic is still enabled (or, with no interests on, topic == "landscape"), still passing the
  filter, and not used since. Delete the rest. Prefer one that fits the mood, else the first. Move its file into the
  wallpaper folder with the normal name. Write back the remaining queue.

---

## 12. Setting the wallpaper on macOS

References for behavior: `WindowsWallpaperSetter.cs`, `VirtualDesktops.cs`, `LockScreenSetter.cs`, `AccentSetter.cs`.

```swift
protocol WallpaperSetter {
    func monitors() -> [MonitorInfo]                 // pixels; id = NSScreen's CGDirectDisplayID as a string
    func setForAll(_ file: URL, fit: WallpaperFit) throws
    func setForMonitor(_ id: String, file: URL, fit: WallpaperFit) throws
    func current(_ id: String?) -> URL?              // NSWorkspace.desktopImageURL(for:)
}
```

- Use `NSWorkspace.shared.setDesktopImageURL(_:for:options:)` for each `NSScreen` (on the main thread).
- Fit → options:

| Fit | `imageScaling` | `allowClipping` | Notes |
|---|---|---|---|
| Fill (default) | `.scaleProportionallyUpOrDown` | `true` | |
| Fit | `.scaleProportionallyUpOrDown` | `false` | `fillColor` = black |
| Stretch | `.scaleAxesIndependently` | – | |
| Center | `.scaleNone` | – | |
| Span | – | – | Oriel does it itself: crop the one big image into one piece per screen (each screen's frame inside the union of all frames, scaled to the image) and set each piece with Fill. Write the pieces next to the file as `{name}_s{n}.jpg`. |

  macOS has no Tile mode in this API: **drop Tile** on Mac (the Fit picker offers Fill, Fit, Stretch, Center, Span).
- **Spaces** (macOS's virtual desktops): the API only changes the current Space on each screen. Windows sets every
  virtual desktop; the Mac equivalent: listen to `NSWorkspace.activeSpaceDidChangeNotification` and, when the newly
  active Space shows a different image than the newest History entry, set it again (cheap, local file). Also re-apply
  on `NSApplication.didChangeScreenParametersNotification` (a display was plugged in).
- Paths must stay valid: never delete the file on screen (retention skips it, §22). macOS caches by URL; Oriel's file
  names are unique per photo so that's fine.
- **Lock screen:** on macOS the lock screen already shows the desktop wallpaper. **Drop** the Windows "Apply to:
  Desktop only / Desktop and lock screen" setting and the onboarding lock-screen card.
- **Accent color:** apps can't set the macOS accent color. **Drop** "Match accent color" (setting + onboarding card).
  **Keep** the accent *sampling*: the `color` field of `sync/current` is the photo's accent ("#RRGGBB"), which other
  devices use. Port `AccentPicker.pick` exactly (`windows/src/Oriel.Core/Wallpaper/AccentPicker.cs`): decode the
  wallpaper to 96×54, 24 hue buckets, skip pixels with V < 0.2, S < 0.25, or (V > 0.95 and S < 0.4), weight = S²·V,
  take the heaviest bucket's weighted mean color, then S = max(S, 0.55) (only if the bucket had weight), V clamped to
  0.55…0.85; a grayscale photo uses its average color; no pixels → `#0078D4`. (Windows reads BGRA; read RGBA on Mac
  and keep the math the same.) Cache the last (path → hex).
- Errors setting the wallpaper → `WallpaperError("macOS refused to set the wallpaper: …")`.

---

## 13. The schedule: one clock for every device

Reference: `windows/src/Oriel.Core/Scheduling/DailyScheduler.cs` (`ScheduleRules`). Android's `Schedule.kt` does the
**identical** math. This must match exactly, or "first device wins" breaks.

Every device of an account changes at the same moments: the **daily time in the shared IANA time zone**, then every
`interval` from there, **starting over at each day's daily time**.

```text
anchor(date, s):   local = date at s.dailyTime in zone(s)
                   if local doesn't exist (DST gap): use local + 1 hour
                   return that instant
localDate(at):     the calendar date of `at` in zone(s)

previousTick(s, now):                     // latest tick at or before now
    anchor = anchor(localDate(now), s); if anchor > now: anchor = anchor(localDate(now) - 1 day, s)
    if no interval: return anchor
    steps = floor((now - anchor) / interval); return anchor + steps * interval

nextTick(s, now):                          // first tick after now
    prev = previousTick(s, now); prevDate = localDate(prev)
    if no interval: return anchor(prevDate + 1 day, s)
    candidate = prev + interval
    sameDay = anchor(prevDate, s); nextAnchor = sameDay > prev ? sameDay : anchor(prevDate + 1 day, s)
    return min(candidate, nextAnchor)

dueTick(s, now) = previousTick(s, now + 30 s)        // a timer firing a moment early still counts
slotId(s, tick) = tick formatted "yyyy-MM-dd'T'HH:mm" in zone(s), en_US_POSIX   e.g. "2026-10-14T09:00"

baseline(state) = lastAppliedAt if it exists and (scheduleStartedAt is nil or lastAppliedAt >= scheduleStartedAt)
                  else scheduleStartedAt
isFresh(s, state, now) = baseline != nil && baseline >= dueTick(s, now)

isDue(s, state, now, isStartupCheck):
    if paused or following: false
    if isFresh: false
    if nextRetryAt != nil && now < nextRetryAt: false
    if isStartupCheck && !applyAtLogin: false
    true

nextCheckIn(s, state, now):                 // how long the scheduler may sleep, clamped to 1 s … 10 min
    if paused: 10 min
    if following: 3 min (the sync interval)
    due = isFresh ? nextTick(s, now) : now
    if nextRetryAt > due: due = nextRetryAt
    clamp(due - now, 1 s, 10 min)
```

Constants: `maxWait = 10 min` (catches sleep/resume and clock changes without system hooks), `syncInterval = 3 min`,
slack 30 s. Use `Calendar(identifier: .gregorian)` with `timeZone = zone(s)`. Verify the DST-gap rule with a test
(e.g. 02:30 on a spring-forward day must become 03:30 local).

Test vectors (from the Windows tests; local zone = the test's zone):
- interval 60, lastAppliedAt 09:00: not due at 09:30 or 09:59; due at 10:00; `nextCheckIn` at 09:50 = 10 min;
  slot at 10:00 = `"2026-10-03T10:00"`.
- daily 09:00, empty state: due at 07:00 (yesterday's 09:00 was missed), both at startup and not.
- lastAppliedAt 09:01: not due at 15:00. lastAppliedAt 08:00: not due at 08:50, due at 09:00.
- applyAtLogin = false, empty state, 07:00, startup → not due.
- lastAppliedAt 08:00, scheduleStartedAt 09:30 → not due at 09:40 (an edit never changes the wallpaper by itself).
- paused or following → never due; nextRetryAt 10:05 → not due at 10:00, due at 10:06.
- nextCheckIn: last 08:00 at 08:56 → 4 min; last 06:00 at 07:00 → 10 min; last 08:00 + retry 10:03 at 10:00 → 3 min;
  last 09:01 at 10:00 → 10 min.
- zone Asia/Tokyo, daily 09:00, now 2026-10-03 00:30 UTC: previousTick = 2026-10-03 00:00 UTC, nextTick =
  2026-10-04 00:00 UTC, slot = `"2026-10-03T09:00"`.

---

## 14. The in-app scheduler

Reference: `DailyScheduler` in `DailyScheduler.cs`.

A single long-running task:

```text
first = true
loop:
    wake = new signal
    wait = 10 min
    try:
        check(isStartup: first)
        if signed in:
            wait = nextCheckIn(settings, state, now)
            poll = pulse.isConnected ? 10 min : 3 min        // live connection up → rare safety poll only
            if wait > poll: wait = poll                      // also while paused: a photo set by hand elsewhere still shows
    catch: log "Scheduler check failed"
    first = false
    sleep until wait elapses OR wake is signalled
```

Wake early on: settings changed, signed in/out. **Mac:** also wake on `NSWorkspace.didWakeNotification` and on
`NSSystemClockDidChange` / `NSSystemTimeZoneDidChange`.

`check(isStartup)`:
1. Once per local day: `retention.cleanup()`.
2. Not signed in → return.
3. `stats.markActive()`.
4. `follow.check()` — the account's (or followed friend's) current photo first: when another device already changed it
   for this tick, that one shows here and no second photo is picked.
5. Following → return.
6. Startup, `applyAtLogin` off, and not fresh → set `scheduleStartedAt = now` (skip the missed one, wait for the next
   tick) and return.
7. Not `isDue` → return.
8. Log "Scheduled change is due (startup=…)", `apply(force: false)`. If `.takenByOtherDevice` → `follow.check()`
   again (the winner's photo arrives through `sync/current`). Fire `attempted(result)` → Home shows the result.

---

## 15. Background fallback (launchd) and login item

References: `ScheduleSync.cs`, `SystemIntegration.cs`, `ScheduledRun.cs`.

Windows has two pieces: **Start with Windows** (Run key → app starts in the tray at sign-in) and a **Task
Scheduler** task that runs `Oriel.exe --apply-today` at the daily time (repeating for slideshows), plus at sign-in
when needed, so the change happens even if the app is closed. A failed run schedules a one-shot retry task.

**Mac:**

1. **Open at login** (`startAtLogin`): `SMAppService.mainApp.register()` / `.unregister()`. When the app is launched
   as a login item it must start **in the background** (menu bar only, no window). Detect it with the launch Apple
   event (`keyAELaunchedAsLogInItem` in `keyAEPropData` of `NSAppleEventManager.shared().currentAppleEvent`); verify
   it works with `SMAppService.mainApp` on macOS 13 and 14+. If it doesn't, register a bundled LaunchAgent via
   `SMAppService.agent(plistName:)` that runs the app executable with `--background`, instead.
2. **Scheduled fallback:** a plist `~/Library/LaunchAgents/uz.akbarjon.oriel.apply.plist`:
   - `ProgramArguments` = [`{current bundle}/Contents/MacOS/Oriel`, `--apply-today`]
   - daily mode: `StartCalendarInterval {Hour, Minute}` = the daily time converted from the shared zone to this Mac's
     local time (launchd uses local time); slideshow: `StartInterval` = interval in seconds (the run itself decides
     with `isDue`, so being approximate is fine);
   - `RunAtLoad` = `applyAtLogin && !startAtLogin` (catch up at login only when the app itself doesn't start then);
   - `ProcessType = Background`, `LowPriorityIO = true`, `Nice = 10`.
   launchd runs a missed `StartCalendarInterval` job after wake (same as Task Scheduler's StartWhenAvailable).
   Register with `launchctl bootout gui/$UID/uz.akbarjon.oriel.apply` (ignore errors) then
   `launchctl bootstrap gui/$UID <plist>`.
   **Paused → remove it** (bootout + delete the file) so nothing runs.
3. **Retry:** after a transient failure in a CLI run, write `uz.akbarjon.oriel.retry.plist` with a
   `StartCalendarInterval` at `nextRetryAt` (local Month/Day/Hour/Minute) and bootstrap it; the run started by it
   removes the retry plist at its start. Replaces any earlier retry.
4. **ScheduleSync:** re-register only when (dailyTime, applyAtLogin, paused, startAtLogin, intervalMinutes) changed
   since the last sync; at app start **force** re-register so the plist points at the bundle that's running now (the
   app may have been moved or updated). Run off the main thread.
5. **App Translocation:** if the app runs from a translocated or quarantined path (opened straight from Downloads),
   the agent path would be wrong. On launch, if the bundle isn't in `/Applications` or `~/Applications`, show a
   one-time alert offering **"Move to Applications"** (copy, relaunch from there, trash the old one).
6. Uninstall: there's no uninstaller on Mac. The app's **Settings > Help** could later offer "Remove Oriel's
   background helpers"; not required for 1.0.

---
## 16. Accounts and sign-in

References: `Auth/AuthService.cs`, `Auth/Account.cs`, `Auth/AccountStore.cs`, `ViewModels/SignInViewModel.cs`.

**Nothing works until the user is signed in.** Signing in happens **on the website**, never inside the app:

1. The app opens `https://oriel.akbarjon.uz/login` (Google) or `https://oriel.akbarjon.uz/login?method=email`
   (email and password) in the default browser.
2. The website signs in and opens **`oriel://auth?token={GoogleIdToken}`** (Google) or
   **`oriel://auth?refresh={FirebaseRefreshToken}`** (email/password account). It may append
   `&share={photoId}&from={Name}` (a shared wallpaper opened before Oriel was installed) and/or `&follow={CODE}` (a
   follow link opened before install).
3. macOS delivers the URL to the app (§23). The app turns it into **its own** Firebase session:
   - Google: `signInWithIdp` (§17.1) → profile + tokens.
   - Refresh token: `token` exchange → tokens, then `accounts:lookup` → profile.
4. **Record this install first**: write `users/{uid}/sessions/{sessionId}` with
   `{platform: "macos", deviceName: Host.current().localizedName ?? "Mac", appVersion: "x.y.z", createdAt, lastSeenAt}`.
   `sessionId` = a new UUID without dashes, lowercase (`Guid.NewGuid().ToString("N")`). If that write fails, nothing is
   saved and the user can just try again.
5. Save the `Account {uid, email, displayName, photoUrl, refreshToken, sessionId, signedInAt}` and fire `changed`.

**Account storage — Mac:** `auth.json` (JSON of `Account`) in the data root with permissions `0600`. Why not the
Keychain: without a paid Developer ID, every update is ad-hoc signed differently and the Keychain would ask the user
for permission after each update. (If Akbar later signs with a stable identity, moving to the Keychain is a small
change.) An unreadable/corrupt file = signed out. `load()` returns nil unless `refreshToken` and `uid` are non-empty.

**Staying signed in:** the app stays signed in, even offline. ID tokens last an hour: `getTokens()` returns cached
tokens if they expire more than 2 minutes from now, else refreshes (and saves the account if the refresh token
rotated). All token work is serialized (one at a time).

**Validation:** 5 s after start, then every **6 hours**: refresh tokens and **touch** the session
(`lastSeenAt`, `appVersion`, with `currentDocument.exists=true`). If the session document is gone (revoked from the
website's /me page or another device) → sign out locally ("This session was removed; signing out"). If Google
rejects the refresh token → sign out. Network errors → stay signed in, try later.

**Sign out:** sets `isSigningOut = true` (the window shows a full-screen "Signing out…" cover), deletes the session
document with an **8-second** timeout (failure is fine: signing out here is what matters), clears the account file,
fires `changed`, then `isSigningOut = false`.

**After sign-in the app:** shows the window and brings it to the front (back from the browser), follows a waiting
follow link, pulls settings and favorites, (re)connects the live listener, publishes the latest wallpaper if the
account has none.

---

## 17. Firebase over REST: every call

Reference: `windows/src/Oriel.Core/Auth/FirebaseClient.cs` (port it 1:1).

Constants:
- Web API key (public; security comes from Auth + rules): `AIzaSyCtUNKnXo3UcKHsG_gtupyf1Tfpo-BzKc8`
- Firestore documents: `https://firestore.googleapis.com/v1/projects/oriel-gcp/databases/(default)/documents`
  (`DocumentsPath` = `projects/oriel-gcp/databases/(default)/documents`)
- Realtime Database: `https://oriel-gcp-default-rtdb.firebaseio.com`
- Firestore calls send `Authorization: Bearer {idToken}`. RTDB calls put `?auth={idToken}` in the URL.

Firestore field encoding: strings `{"stringValue": …}`, booleans `{"booleanValue": …}`, integers
`{"integerValue": "123"}` (a string!), timestamps `{"timestampValue": "2026-10-14T09:00:00.0000000Z"}` (ISO 8601 UTC),
arrays `{"arrayValue": {"values": [...]}}`, maps `{"mapValue": {"fields": {...}}}`.

Errors: transport error/timeout → `FirebaseError.network("Couldn't reach Google. Check your connection.")`. HTTP
404 → `.notFound`; 429 → `.other`; other 4xx → `.rejected`; 5xx → `.other`. The message is
`"{error.status} {error.message}"` from the JSON body (or "Unexpected response from Google."). If the message contains
`NOT_FOUND` or `FAILED_PRECONDITION`, it's `.notFound` (conditional writes on a missing document report 400 or 404).

### 17.1 Auth

| Purpose | Request |
|---|---|
| Google ID token → session | `POST https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key={KEY}` body `{"postBody":"id_token={escaped}&providerId=google.com","requestUri":"http://localhost","returnSecureToken":true,"returnIdpCredential":true}` → `localId, idToken, refreshToken, expiresIn, email, displayName, photoUrl` |
| Refresh | `POST https://securetoken.googleapis.com/v1/token?key={KEY}` form `grant_type=refresh_token&refresh_token=…` → `id_token, refresh_token, expires_in, user_id` |
| Profile for a refresh-token sign-in | `POST https://identitytoolkit.googleapis.com/v1/accounts:lookup?key={KEY}` body `{"idToken":"…"}` → `users[0].email, displayName, photoUrl` (none → rejected "Account not found.") |

`expiresAt = now + expiresIn seconds` (default 3600).

### 17.2 Documents (all writes are `PATCH {url}` with body `{"fields": {...}}`)

| Purpose | URL / notes |
|---|---|
| Session create/replace | `users/{uid}/sessions/{sessionId}`: platform, deviceName, appVersion, createdAt, lastSeenAt |
| Session touch | same + `?updateMask.fieldPaths=lastSeenAt&updateMask.fieldPaths=appVersion&currentDocument.exists=true`; `.notFound` → return false (revoked) |
| Session delete | `DELETE users/{uid}/sessions/{sessionId}` (404 is fine) |
| Publish current photo | `users/{uid}/sync/current`: photoId, imageUrl, pageUrl, photographer, photographerUrl, source (**"macos"**), updatedAt (now), plus description, topic, color only when non-blank |
| Has current? | `GET users/{uid}/sync/current?mask.fieldPaths=photoId` (404 → false) |
| Current photo id of an owner | `GET users/{ownerUid}/sync/current?mask.fieldPaths=photoId` → `photoId` or nil (404). A follower whose access ended gets `.rejected` |
| Read shared settings | `GET users/{uid}/sync/settings` → see §18.2; nil on 404 |
| Write shared settings | `users/{uid}/sync/settings`: dailyTime, timeZone, intervalMinutes (int), paused, favoritesOnly, skipLowLikes, minLikes (int), matchTimeOfDay, interests (array of maps `{query, enabled, isCustom}`), updatedAt, device (sessionId) |
| Claim slot | §18.4 |
| Favorite write | `users/{uid}/favorites/{escaped photoId}`: photoId, imageUrl, pageUrl, photographer, photographerUrl, favorite (bool), updatedAt, device, description (if non-blank) |
| Favorites since | `POST users/{uid}:runQuery` body `{"structuredQuery":{"from":[{"collectionId":"favorites"}],"where":{"fieldFilter":{"field":{"fieldPath":"updatedAt"},"op":"GREATER_THAN","value":{"timestampValue":"…"}}}}}` (no `where` when `since` is nil). Response is an array of `{document:{fields}}` rows; skip rows without `document`, `photoId` or `imageUrl` |
| Follow code lookup | `GET codes/{code}` → `{uid, name, picture}` or nil |
| Follow code create | `codes/{code}?currentDocument.exists=false`: uid, name, picture, createdAt |
| Follow code photo | `codes/{code}?currentDocument.exists=true&updateMask.fieldPaths=photoId&updateMask.fieldPaths=photoPath&updateMask.fieldPaths=photographer&updateMask.fieldPaths=photographerHandle` |
| Follow code delete | `DELETE codes/{code}` |
| Share create | `shares/{id}?currentDocument.exists=false`: photoId, photoPath, photographer, photographerHandle, uid, from, picture, createdAt |
| Add follower | `users/{friendUid}/followers/{myUid}`: code, name, createdAt |
| Remove follower | `DELETE users/{ownerUid}/followers/{followerUid}` |
| List followers | `GET users/{uid}/followers?pageSize=300&mask.fieldPaths=name&mask.fieldPaths=createdAt` → id = last path segment of `name`; blank name → "A friend"; newest first |
| Stats +1 | `POST {DocumentsUrl}:commit` body `{"writes":[{"update":{"name":"{DocumentsPath}/stats/{yyyy-MM-dd}","fields":{}},"updateMask":{"fieldPaths":[]},"updateTransforms":[{"fieldPath":"macos_active","increment":{"integerValue":"1"}}, …]}]}` |

### 17.3 Realtime Database doorbell (`pulse/{uid}`)

`PATCH {RTDB}/pulse/{uid}.json?auth={idToken}` (**PATCH, never PUT**, so photo, settings and favorites rings never
clobber each other). Timestamp with `{".sv": "timestamp"}`.

| Ring | Body |
|---|---|
| Photo | `{"photoId": id, "source": "macos", "device": sessionId, "at": {".sv":"timestamp"}}` |
| Settings | `{"settingsAt": {".sv":"timestamp"}, "settingsDevice": sessionId}` |
| Favorites | `{"favoritesAt": {".sv":"timestamp"}, "favoritesDevice": sessionId}` |

Listening: `GET {RTDB}/pulse/{uid}.json?auth={idToken}` with `Accept: text/event-stream` (§18.5).

---

## 18. Cross-device sync

The protocol is shared by every platform. Get it exactly right.

### 18.1 Current photo (`SyncPublisher`)

Reference: `Services/SyncPublisher.cs`.

- Every change on this device (scheduled, Change now, Search, History "Set again", wallpaper of the day, a shared
  link, a followed friend's photo) is published to `users/{uid}/sync/current`, then the doorbell rings.
- Only Unsplash photos (thumbnail host `images.unsplash.com`); `imageUrl` = the entry's thumbnail URL (raw Unsplash
  URL; each device crops its own). Sync the **photo ID**, never the image file.
- `color` = the accent sampled from the local file (§12), or omitted.
- 8 s timeout for the whole publish. Never throws: log "Couldn't share the wallpaper {id}: {reason}".
- After a successful write: ring (failure logged: "Couldn't ring the other devices (they still poll)"); then, **if
  sharing with friends** (`myFollowCode` set), update `codes/{code}` photo fields so the follow page shows the new
  wallpaper.
- `publishLatestIfMissing()`: at app start and after each sign-in, if the account has **no** `sync/current` yet,
  publish the newest history entry (no ring, no follow-page update) so a newly linked extension has something to show.
- **No echo:** a photo that came from another device of this account is applied with `publish: false`. A followed
  friend's photo **is** published (so the account's other devices get it too).

### 18.2 Shared settings (`SettingsSync`)

Reference: `Services/SettingsSync.cs`.

`SyncedSettings = {dailyTime, timeZone, intervalMinutes, paused, favoritesOnly, skipLowLikes, minLikes, interests[{query, enabled, isCustom}], matchTimeOfDay}`.
- From local: interests = non-blank topics, trimmed.
- Apply to local: copy every field; rebuild topics from interests **keeping each custom interest's local `coverUrl`**
  (case-insensitive match by query); then `updateBuiltInTopics()`.
- `sameSchedule` compares dailyTime, timeZone, intervalMinutes, paused.
- `fingerprint` = a stable JSON encoding of the record (sorted keys). Used to tell a real change from an echo.
- Reading the remote doc: missing `dailyTime` → "09:00", `timeZone` → "UTC", `intervalMinutes` → 0, `minLikes` → 50,
  booleans → false, **`matchTimeOfDay` missing → true**; interests without a query are skipped; `updatedAt` missing →
  distant past.

Flow:
- `start()`: remember `last = from(local)`; subscribe to local settings changes; on sign-in → `pull()`; `pull()` now.
- **Local change:** `now = from(local)`. If the schedule part changed vs `last` → `state.scheduleStartedAt = now`
  (an edit never triggers a catch-up change). `last = now`. If `now.fingerprint == state.syncedSettingsHash` → it
  came from the account, don't send. Otherwise **debounce 1.2 s** then `push()`.
- `push()`: signed in → write `sync/settings` (device = sessionId, updatedAt = now UTC), save
  `syncedSettingsHash`, `settingsSyncedAt = updatedAt`, `settingsDirty = false`, log "Shared your settings with your
  other devices", ring settings. Offline/error → `settingsDirty = true` (sent at the next pull).
- `pull()`: read remote. **None** → this is the first device: push ours. Same fingerprint → record hash/time, clear
  dirty. If `settingsDirty` and our `settingsSyncedAt >= remote.updatedAt` → ours is newer: push. Else adopt theirs:
  record hash/time, clear dirty, apply to local settings and save (which triggers the local-change handler: a
  schedule change starts fresh, and nothing is sent back because the hash matches). Log "Took the settings changed on
  another device".
- First sign-in therefore adopts the account's settings, or uploads its own if the account has none.
- **Device-only, never synced:** fit, photo per display, folder, open at login, change at login, hotkey, follow
  state, onboarding, search history.

### 18.3 Favorites (`FavoritesSync`)

Reference: `Services/FavoritesSync.cs`.

- `start()`: listen to `history.favoriteChanged`; on sign-in → `pull()`; `pull()` now.
- **Local star/unstar** (not while applying remote changes): write `favorites/{photoId}` with
  `favorite = isFavorite`, `updatedAt = now`, `imageUrl = https://images.unsplash.com{thumbnail path}` (no query), then
  ring favorites. Never deleted: un-starring writes `favorite: false` so it syncs.
- `pull()` (one at a time, skip if one is running): `since = state.favoritesSyncedAt`; fetch changes since;
  apply them oldest first, "quietly":
  - if the photo is in local history (any rows, deleted included): set favorite on each row that differs;
  - else if `favorite` is true: add a history row: provider unsplash, filePath `""`,
    `thumbnailUrl = imageUrl + (? or &) + "w=640&q=80&fm=jpg&fit=max"`, topic `"favorite"`, isFavorite true,
    `appliedAt` = its `updatedAt`, but if that's newer than the current newest entry, use (newest − 1 s) so it
    **never becomes the current wallpaper**. ("Set again" downloads it later.)
  - First sync on this device (`since == nil`): upload every local favorite the account doesn't have (no ring each),
    then one favorites ring.
  - `favoritesSyncedAt = max(updatedAt of changes)` or keep `since` or Unix epoch.
  - Log "Took {n} favorite change(s) from your other devices".

### 18.4 First device wins (`sync/slot`)

Reference: `FirebaseClient.ClaimSlotAsync`, `SlotClaimer`.

Up to 3 attempts:
1. `GET users/{uid}/sync/slot`. 404 → precondition `currentDocument.exists=false`. Else if its `slotId` equals ours →
   **return false** (another device has it). Else precondition `currentDocument.updateTime={that doc's updateTime, URL-escaped}`.
2. `PATCH users/{uid}/sync/slot?{precondition}` with `{slotId, device: sessionId, at: now}` → **return true**.
3. `.notFound` / `.rejected` (someone wrote in between) → loop.
After 3 tries → false. Any Firebase error at the claimer level → return **nil** ("changing anyway"). Log
"This device takes the {slot} change" / "Another device already took the {slot} change".

### 18.5 Live doorbell (`PulseListener`)

Reference: `Services/PulseListener.cs`.

- One SSE connection while signed in (an `URLSession` with no request timeout). Reconnect loop: after a normal end
  (the hourly token expired → `auth_revoked`/`cancel` event) reconnect at once with fresh tokens; after an error back
  off 2 s, doubling up to 120 s; not signed in → wait 30 s; sign-in/out → drop the connection and start over.
- `isConnected` = true while the stream is open (the scheduler then polls only every 10 min instead of 3).
- Parse lines: `event: X` then `data: {...}`. `put` / `patch` → handle; `auth_revoked` / `cancel` → end and reconnect.
- Event data: `{"path": "/", "data": {...}}`. Only handle when `data` is an object.
  - `photo` = has `photoId` and (`initial` (the first `put`) or `device != mySessionId`)
  - `prefs` = has `settingsAt` and (`initial` or `settingsDevice != me`)
  - `stars` = has `favoritesAt` and (`initial` or `favoritesDevice != me`)
  - stars → `favorites.pull()`. prefs → `settings.pull()` then (if photo) `follow.check()`. Else photo →
    `follow.check()`. (Settings first: a new schedule decides whether the photo check should change anything.)
- Log "Listening for wallpapers from your other devices".
- Spark allows 100 simultaneous RTDB connections; beyond that devices silently fall back to polling. Fine.

### 18.6 Receiving a photo (`FollowService.check`)

Reference: `Services/FollowService.cs` (`CheckAsync`). One at a time (skip if running).

1. tokens; `owner = followUid ?? myUid`.
2. `photoId = currentPhotoId(owner)`. A `.rejected` while following → problem "{FriendName or "Your friend"} stopped sharing their wallpaper." → return.
3. nil or equal to `state.followedPhotoId` → clear any problem, return.
4. Already the newest history entry here (this device published it or showed it) → record `followedPhotoId`, return.
5. Phase `.receiving`; `getSharedPhoto(photoId)` (on error: `finishPhase` and rethrow → logged).
6. `applyPhoto(photo, topic: following ? "from {FollowName}" : "synced", publish: following)`.
7. On success: `followedPhotoId = photoId`, clear problem, log, fire `changed` (Home refreshes).
8. Firebase/provider errors are logged: "Couldn't check the followed wallpaper: …".

**Lock (`paused`) only stops the schedule**: a photo set by hand on any device still shows everywhere, and receiving
one never unlocks.

When it runs: app start (scheduler's first check), every scheduler loop (≤ 3 min without SSE, ≤ 10 min with), each
doorbell ring, before each scheduled change, after losing a slot.

---

## 19. Friends: follow and share

Reference: `Services/FollowService.cs`, `Services/ShareService.cs`, `ViewModels/FriendsViewModel.cs`.

**Codes:** 8 chars from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (no 0/O, 1/I), cryptographically random.
- `normalize(input)`: uppercase, drop whitespace and `-`; valid if exactly 8 chars from the alphabet, else nil.
- `format(code)` = `K7MQ-2XPA`.
- `formatInput(typing)`: uppercase, keep only ASCII letters/digits, cut at 8, insert `-` after the 4th
  (`k7mq2x` → `K7MQ-2X`). Applied live as the user types/pastes, caret at the end.

**Share my wallpaper (be followed):**
- `ensureCode()` = `myFollowCode` or `startSharing()`.
- `startSharing()`: up to 4 tries with new codes: create `codes/{code}` (name = my clean first name or "A friend",
  picture = Google picture or ""), save `myFollowCode`, put the current wallpaper on the follow page. A code taken
  or refused (non-network error) → try another; network → "You seem to be offline. Try again."; else "Couldn't create a code. Try again."
- Link = `https://oriel.akbarjon.uz/follow/{code}`. After this, every publish updates the code's photo (§18.1).
- `stopSharing()`: delete the code, remove every follower (list + delete each), clear `myFollowCode`. Errors:
  offline / "Couldn't stop sharing. Try again."
- `listFollowers()` → nil on error. `removeFollower(uid)`: errors "You seem to be offline. Try again." / "Couldn't remove them. Try again."

**Follow a friend:**
- `follow(input)`: invalid → "A code has 8 letters and numbers, like K7MQ-2XPA.". Look up the code; none →
  "No one has that code. Check it and try again."; own → "That's your own code. Send it to a friend instead.".
  If already following someone else, leave them first (remove myself from their followers; errors only logged).
  Add myself to their followers (code, my first name). Save followUid/Name ("your friend" if blank)/Picture. Clear
  `followedPhotoId` and `followProblem`. Run `check()` (their photo is set now). Return the name.
  Firebase errors: offline / "Couldn't follow with that code. Try again."
- While following, **the own schedule is off** (scheduler and apply skip; prefetch off).
- `unfollow()`: remove myself from their followers (errors ignored), clear follow fields and follow state. Own
  schedule takes over.
- **Follow links** (`oriel://follow?code=` or `oriel://auth?...&follow=`): `followFromLink(code)` remembers
  `linkCode`, clears `linkError`, fires `changed`; if signed in, follows now; else after sign-in
  (`followWaitingLink()`). The Friends page shows "Following your friend…" while `linkCode` is set, and the error
  once if it fails. The window opens on **Friends** when a follow link arrives.

**Share one photo (Home's Share button):** `ShareService.link(entry)`: only Unsplash photos ("Only photos from
Unsplash can be shared."); one link per photo per app run (cache photoId → link); create `shares/{id}` with a new
8-char id (up to 4 tries), `from` = my clean first name, `picture` = Google picture → `https://oriel.akbarjon.uz/share/{id}`.
Not signed in → "Sign in to Oriel first."; offline → "You seem to be offline. Try again."; else "Couldn't make a link. Try again."

The website pages `/share/{id}` and `/follow/{code}` open `oriel://wallpaper?id={photoId}&from={Name}` and
`oriel://follow?code={CODE}`.

---

## 20. Wallpaper of the day

Reference: `Services/TodayService.cs`, Home's "Wallpaper of the day" card.

- List: `GET https://oriel.akbarjon.uz/today.json` → `{"rule": "...", "photos": [{"id","path","by","handle"}]}`.
  Cache it in `today.json` in the data root; re-download when the file is older than **3 days** (offline → use the
  cached file). Keep valid entries (id and path non-empty).
- Today's photo = `photos[daysSince1970(local date) mod count]` (positive modulo), the same rule as the website's
  `/today` page, so it's the same photo everywhere.
- Thumbnail: `https://images.unsplash.com/{path}?w={width}&q=75&fm=jpg&auto=format&fit=crop` (Home uses w=480,
  decoded at 240 pt wide). Author link `https://unsplash.com/@{handle}?utm_source=oriel&utm_medium=referral`.
- "Set as wallpaper" → `getSharedPhoto(id)` + `applyPhoto(photo, topic: "wallpaper of the day")`.
- Loaded once per local day when Home is shown.

---

## 21. Anonymous daily stats

Reference: `Services/StatsReporter.cs`.

One small document per UTC day `stats/{yyyy-MM-dd}`, numbers only, no user IDs, no per-click events.

- Platform prefix on Mac: **`macos`** → fields `macos_active`, `macos_new` (needs the rules change in §46).
- `stats.json` memory: `{firstDay, activeDay, retained7, retained30}`.
- `markActive()` (scheduler, signed in, one at a time): if `activeDay == today (UTC)` → nothing. Else fields =
  [`macos_active`]; if `firstDay` is nil: if history has an older entry (an existing install seen for the first time),
  `firstDay` = the date of the oldest history entry and it's not "new"; else `firstDay = today` and add `macos_new`.
  Age ≥ 7 and not counted → `retained7`; ≥ 30 → `retained30`. Send (8 s timeout); remember only if sent.
- `count(field)` (fire and forget): `sharesSent` (Home Share link went out), `sharesOpened` (a shared photo card
  opened), `sharesSet` (a shared photo was set).

---

## 22. Retention and the wallpaper folder

References: `RetentionService.cs`, `WallpaperFolderMover.cs`.

- Once a day (scheduler) and after a successful CLI run: delete files of **non-favorite** entries older than
  **30 days** (**7 days** in slideshow mode, which makes many). Never delete a file that's on screen (check each
  screen's current desktop image). Mark rows deleted (they stay, so photos never repeat). Log "Retention removed {n} old image(s)".
- History **Delete**: delete the file, mark deleted.
- **Change folder** (Settings > Storage): the user picks a folder (NSOpenPanel, directories only, can create, starts
  in ~/Pictures). Same folder → nothing. Non-empty target → "Pick an empty folder. Oriel moves your photos into it."
  Take the apply lock ("A wallpaper change is in progress. Try again in a moment."). Move every file except `*.part`;
  on any failure move the moved ones back and throw "Couldn't move the photos: {reason} Nothing was changed.".
  Update history paths (one transaction), save the setting, **re-point the current wallpaper** (set the moved file
  again on each screen that showed an old path), delete the old folder if empty. Result: "Moved 1 photo." /
  "Moved {n} photos." (success banner) or the error (error banner).

---

## 23. Deep links (oriel://)

Reference: `Auth/DeepLink.cs`, `App.xaml.cs` (`HandleLink`).

Register the `oriel` URL scheme in Info.plist (`CFBundleURLTypes`, name "Oriel"). Receive URLs in
`NSApplicationDelegate.application(_:open:)` (both at launch and while running). Accept scheme `oriel`
case-insensitively; the host is the command.

| Link | Action |
|---|---|
| `oriel://open` | Just show the window (the extension's "Open Oriel" button) |
| `oriel://wallpaper?id={id}&from={Name}` | Open the **shared photo card** (§36). `id` must match `^[A-Za-z0-9_-]{6,24}$`; `from` = cleanName |
| `oriel://follow?code={CODE}` | Follow (§19), open on Friends |
| `oriel://auth?token={googleIdToken}` | Complete Google sign-in |
| `oriel://auth?refresh={refreshToken}` | Complete email/password sign-in |
| `…auth…&share={id}&from={Name}` | After sign-in, open the shared photo card |
| `…auth…&follow={CODE}` | Follow once signed in, open on Friends |

Order inside `handleLink`: `open` → show. `wallpaper` → shared card. Follow code present (follow or auth host) →
remember "open Friends", `followFromLink`. Then: token → `signIn.complete(token)`; else refresh → `completeWithSession`;
else if no follow code → `signIn.invalid()` ("That sign-in link wasn't valid. Sign in again from the website.").
Then pending share → open the shared card. Any oriel link shows the window.

Sign-in completion (`SignInViewModel.complete`): ignore if already working or already signed in. Errors:
network → the network message; rejected → "That sign-in expired or isn't valid. Sign in again from the website.";
other → "Couldn't sign you in right now. Please try again."

---

## 24. App lifecycle: menu bar app, window, single instance

Reference: `Program.cs`, `App.xaml.cs`, `MainWindow.xaml.cs`.

- **It lives in the menu bar.** The window is a normal window that's **destroyed when closed** (not hidden), so its
  view tree and images are freed; the process keeps running with just the menu bar icon.
  - Window open → `NSApp.setActivationPolicy(.regular)` (Dock icon, app menu, Cmd+Tab).
  - Window closed → `.accessory` (menu bar only), release Home/History/Search images, let memory go.
  - `applicationShouldTerminateAfterLastWindowClosed` → false.
  - Clicking the Dock icon while the window is closed (`applicationShouldHandleReopen`) → show the window.
  - **Cmd+Q / "Quit" quits for real** (Mac convention). The launchd fallback keeps the schedule running.
- **Single instance:** LaunchServices already routes Finder launches and `oriel://` URLs to the running instance.
  A CLI invocation of the binary (`--apply-today` etc.) is a separate short-lived process (that's intended; the apply
  lock prevents clashes). If a second GUI instance starts anyway (e.g. launched from a different copy), it activates
  the existing one (`NSRunningApplication.runningApplications(withBundleIdentifier:)`) and exits.
- **Startup sequence** (mirror `App.OnLaunched` order):
  1. Parse CLI args; if a CLI command → run headless (§41) and exit.
  2. Build services (composition root `AppHost`). Logging to file.
  3. Create the menu bar icon (open / change now / quit).
  4. Register the global shortcut from settings; log "Shortcut registered: {ok|error}".
  5. Subscribe: `wallpapers.applied` → refresh Home if the window is visible; (updates are Sparkle's job).
  6. Start the scheduler; its `attempted` → Home shows the result.
  7. `wallpapers.prefetchEnabled = true`; prefetch in the background.
  8. `follow.changed` → refresh Home.
  9. Settings changed → `scheduleSync.sync()` (background); force sync now.
  10. Start the updater (Sparkle).
  11. Auth `changed` → if signed in: show the window (front), follow a waiting link, publish latest if missing.
      `auth.start()` (validation timer).
  12. Start the pulse listener, settings sync, favorites sync; `publishLatestIfMissing()`.
  13. Decide: launched as login item / `--background` → stay in the background. **A sign-in link, or not being signed
      in, always shows the window** ("nothing works until signed in: ask right away").
  14. Log "Oriel started (background={…})".
- **Main window**: §27. When the window becomes key (activated) → refresh Home.
- **Unhandled UI errors** are logged and swallowed where possible (a menu bar app should survive a bad click).

---
## 25. Design system

References: `Styles/Theme.xaml`, `Styles/Colors.Light.xaml`, `Styles/Colors.Dark.xaml`, `Styles/OnPhoto.xaml`.

Oriel's look, shared with the website: **ink on paper, softly rounded, no borders.** Black and white; the only color
is the photos and the gold favorite star. Follows the system light/dark appearance automatically.

### 25.1 Colors (define as named colors with light/dark variants)

| Token | Light | Dark | Used for |
|---|---|---|---|
| `paper` | `#FAFAFA` | `#0F0F0F` | Window/page background, text on primary buttons |
| `surface` | `#F1F1F1` | `#1A1A1A` | Cards, rows, plain buttons, search bar |
| `line` | `#E4E4E4` | `#262626` | Hover/pressed of plain buttons, dividers, image placeholders |
| `muted` | `#6B6B6B` | `#9A9A9A` | Secondary text, row icons |
| `ink` | `#141414` | `#F2F2F2` | Text, primary button background, selection marks |
| `inkHover` | `#363636` | `#D6D6D6` | Primary button hover/pressed |
| `star` | `#F5B301` | same | Favorite star |
| photo tile background | `#1C1C1C` | same | Interest tiles, Following card (always dark) |
| scrim | linear gradient top→bottom: 0 → `#00000000`, 0.45 → `#59000000`, 1 → `#CC000000` | same | Bottom of photo cards, keeps white text readable |
| Google button | bg `#F2F2F2`, hover `#E4E4E4`, pressed `#D8D8D8`, text `#141414`, disabled text `#6B6B6B` | same in both themes | |
| On-photo light pill (Search results "Set as wallpaper") | bg `#F2F2F2`, hover `#E0E0E0`, pressed `#D0D0D0`, text `#141414` | same | |
| On-photo icon buttons (History grid) | bg `#2EFFFFFF`, hover `#4DFFFFFF`, pressed `#66FFFFFF`, disabled `#1AFFFFFF`; icons/text white (disabled `#80FFFFFF`); links white, hover `#E0E0E0`, pressed `#C0C0C0` | same | |

The system accent (blue) must not appear: the app's tint is `ink`. Primary = ink background + paper text.
Toggles (switches): use `.tint(ink)`.

**Window background:** the sidebar uses the native translucent sidebar material (the Mac equivalent of Windows'
Mica); the content area is `paper`.

### 25.2 Shape

| Token | Radius | Used for |
|---|---|---|
| pill | 12 | Buttons, inputs, chips, pickers |
| icon | 18 | Circle icon buttons (36×36) |
| tabItem | 9 | Selected item inside a 3 px padded segmented strip (concentric with 12) |
| control | 8 | Small controls |
| medium | 12 | List rows, banners |
| card | 16 | Cards, setting rows, photo cards |
| large | 20 | Home preview, big cards, dialogs |
| thumb | 8 | Small thumbnails |
| bar | 26 | The 52 pt search bar |

Nested shapes use outer radius minus padding. Use continuous (squircle) corners on Mac.

### 25.3 Type (SF Pro, the system font)

| Style | Size / weight | Extras |
|---|---|---|
| Page title | 30, semibold | tracking −0.6 (≈ −2%), top margin 24, bottom 4 |
| Section header | 15, semibold | top margin 28, bottom 8, left 2 |
| Body | 14 regular | |
| Row title | 14 regular (semibold where noted) | |
| Description / muted | 12–13, `muted` | wraps |
| Small meta | 12 | |

### 25.4 Layout

- Page content max width **960**, centered; margins 40 left/right, 8 top, 40 bottom (Search/Interests: 32 top).
- Setting rows spacing 6.
- Buttons: plain pill = `surface` bg, hover `line`, no border, padding 16×7, radius 12. Primary pill = `ink` bg,
  `paper` text, hover `inkHover`. Icon button = 36×36 circle. No borders anywhere (a transparent border makes buttons
  look smaller side by side).
- **InfoBar** (Windows' banner) → a rounded (12) banner with an SF Symbol for severity (info `info.circle.fill`,
  success `checkmark.circle.fill`, warning `exclamationmark.triangle.fill`, error `xmark.octagon.fill`), bold title,
  message, optional close ✕. Background: the severity color at ~12% over `surface`; icon in the severity color
  (system yellow/red/green/blue are OK here, it's the only place).
- **SettingRow** control: min height 64, padding 20/14/16/14, `surface`, radius 16, columns: icon (18 pt, `muted`),
  text (title 14, description 12 `muted`, hidden when empty so single-line rows stay centered), control on the right.
  The text takes only the space the control leaves and wraps.

---

## 26. Motion: every animation

Respect **Reduce Motion** (`NSWorkspace.shared.accessibilityDisplayShouldReduceMotion`): skip movement, keep simple
fades or nothing. All "cubic ease-out" = `cubic-bezier(0.215, 0.61, 0.355, 1)` or `.easeOut`; "ease-in" mirrors it.

| # | Where | Exactly |
|---|---|---|
| M1 | **Image reveal** (every photo: Home preview, Today thumb, search results, history, interest tiles, onboarding hero) | Starts at opacity 0 and scale **1.1** around its center, animates to 1/1: opacity over **450 ms**, scale over **700 ms**, easing `cubic-bezier(0.2, 0.8, 0.2, 1)`. A remote image stays hidden until it has loaded, then reveals. An image already revealed once (same source, e.g. going back to a page, History ↔ Favorites) just appears. |
| M2 | **Star burst** (Home, "TikTok favorite") | See §30.3 |
| M3 | **Change button label swap** (Home) | Old words: opacity 1→0 and y 0→**−8** over **130 ms** ease-in. Then the new words start at y **+8**, go to 0 and opacity 1 over **130 ms** ease-out. If a newer word arrives mid-way, only the newest is shown. |
| M4 | **Sidebar icon on click** | Home/Favorites/Friends/Settings "hop and pop": scale 1→**1.28** at 140 ms →1 at 300 ms, y 0→**−3** at 140 ms →0 at 300 ms (quadratic ease-in-out). Search "magnifier wiggle": rotation 0→**−18°** at 110 ms →**14°** at 260 ms →0 at 400 ms, scale 1→1.2 at 150 ms →1 at 400 ms. History "winds back a turn": rotation 0→**−360°** over **550 ms** cubic ease-out. A new click restarts it. |
| M5 | **Onboarding step change** | The new step slides in from **+28 pt** (forward) or **−28 pt** (back) to 0 over **320 ms** and fades 0→1 over **260 ms**, cubic ease-out. |
| M6 | **Onboarding progress line** | Grows/shrinks (scaleX from the left) to the new value over **450 ms** cubic ease-out. |
| M7 | **Interest tile hover** (Interests page + onboarding) | The photo (not the tile) scales to **1.06** around its center on hover, back to 1 on exit, **300 ms**. Nothing else flashes on hover/press. |
| M8 | **Change popup** | §39 |
| M9 | **Page navigation** | A gentle entrance (fade + small rise, ~200 ms) when switching pages; Search → Interests slides in from the right; back slides out to the right. |
| M10 | **History card actions** | Hidden (opacity 0) until the pointer is over the card or it has keyboard focus, then opacity 1 instantly. |
| M11 | **Share button** | After the link is copied (fallback), the share icon becomes a check mark for **2 s**. |
| M12 | **Segmented pill (History grid/list)** | The ink highlight sits under the selected item; it moves without animation. |

---

## 27. Main window and navigation

Reference: `MainWindow.xaml(.cs)`.

- Size **1100 × 780** points, centered on the main screen's visible frame (clamped to it); minimum **560 × 480**.
- Title **"Oriel (Beta)"** with the Oriel logo. Use a unified/transparent title bar with the sidebar going up into
  it (standard Mac sidebar app look).
- **Sidebar** (`NavigationSplitView` or `NSSplitViewController` with a sidebar), items with SF Symbols:
  1. **Home** (`house`) — selected at start
  2. **Search** (`magnifyingglass`)
  3. **History** (`clock.arrow.circlepath`)
  4. **Favorites** (`star`)
  5. **Friends** (`person.2`)
  6. **Settings** (`gearshape`) pinned at the bottom of the sidebar. Also open it with **Cmd+,**.
  Selected item background: `surface`; hover: `surface`; selected+hover: `line`; indicator/text `ink`.
  Windows starts with the pane collapsed to icons; on Mac a normal sidebar (~200 pt) that the user can collapse is fine.
- Clicking an item plays its icon animation (M4). Clicking the current item again leaves a sub-page (Interests) and
  returns to its main page.
- History and Favorites are **one page** with a mode; switching between them reloads that page in place.
- **Overlays** (cover the content area, sidebar hidden):
  - not signed in → **Sign-in screen** (§28) covers everything;
  - signed in and the tour should show → **Onboarding** (§29);
  - signed in, no tour, a shared photo is open → **Shared photo card** (§36) over the app;
  - signing out → full-window `paper` cover with a 32 pt spinner and "Signing out…" (nothing clickable).
  Re-evaluate on sign-in/out, tour finished, shared card opened/closed. When the app becomes usable, refresh Home.

---

## 28. Sign-in screen

Reference: `Controls/SignInView.xaml`, `ViewModels/SignInViewModel.cs`.

Centered column, max width 360, margin 24, spacing 12:
1. Oriel logo 56×56.
2. Title "Sign in to Oriel" (page title style, margin 12 top / 8 bottom).
3. Error banner (error severity, not closable) when there's an error.
4. **Google button**: full width, height 44, radius 22, colors in §25.1 (same in both themes), Google "G" 18×18 +
   "Continue with Google" (medium weight); while completing: "Continuing with Google…" and disabled.
   Action: open `https://oriel.akbarjon.uz/login`.
5. Link button "Use email and password instead" (13 pt): open `https://oriel.akbarjon.uz/login?method=email`.
6. While waiting: "Waiting for you to finish in the browser…" (muted).
7. Support link (12 pt, chat bubble icon): "Need help? Message support on Telegram" → `https://t.me/oriel_support_bot`.

If the browser can't be opened: "Couldn't open your browser. Go to oriel.akbarjon.uz/login yourself."

---

## 29. Onboarding tour (first run)

Reference: `Controls/OnboardingView.xaml(.cs)`, `ViewModels/OnboardingViewModel.cs`. Read these if you can: this is
the part people see first.

**When:** right after the first sign-in, covering the app. `shouldShow()`: false once finished/skipped in this run;
true if `ORIEL_ONBOARDING=1`; false if `onboardingDone`; true if History is empty; otherwise (an existing user) mark
`onboardingDone = true` and return false. **Nothing is saved until the end**, so skipping keeps every default.

**Layout:** a scroll view; one centered column, width = min(640, windowWidth − 64), min height 560, vertical margin
28, three rows with spacing 28: header / step content (vertically centered) / footer.

**Header** (min height 32, column spacing 14): on steps 1–3 only: "1/3" (13 semibold; accessibility label
"Step 1 of 3"), a progress line (height 4, radius 2, track `line`, fill `ink`, animated M6 to step/3), and on steps 0–3
a muted link "Skip setup" on the right.

**Steps** (each slides in, M5):

**Step 0 — Welcome** (spacing 26)
- Three photos in a row, height 168, columns 1 : 1.3 : 1, spacing 10, radius 16, `surface` placeholder; the two
  side photos have a top margin of 18 (they sit lower, a gentle arc). Photos: the covers of **mountains**,
  **northern lights**, **ocean** (Appendix B, width 600, decoded at ~360 pt). Reveal M1.
- Logo 36 + title "Welcome to Oriel" (page title, no margin) side by side, spacing 12.
- Muted: "A beautiful new wallpaper, picked for you. Setting up takes under a minute."
- Three lines (spacing 14), each: icon tile 40×40 radius 12 `surface` with a 16 pt icon, then title (semibold) and a
  13 pt muted line:
  - `calendar` — "A new photo, on your schedule" / "Every day or every few hours, or whenever you press Change now."
  - `heart` — "Only what you love" / "You pick the interests. Star a favorite to keep it forever."
  - `moon` — "Quiet and light" / **Mac:** "Oriel waits in the menu bar and only wakes up to change your wallpaper."

**Step 1 — Interests** (spacing 18)
- Title "What do you like?" / muted "Pick a few. Each new wallpaper comes from one of them."
- Grid of the 24 built-in interests as **photo tiles**: adaptive columns, min item width 140, height 92, spacing 10,
  radius 14, background `#1C1C1C`, always dark styling:
  - the cover photo (Appendix B, width 400, decoded ~280 pt) fills the tile (M1 reveal, M7 hover zoom);
  - scrim on top; label bottom-left (semibold, white, margin 12/0/12/10, truncates);
  - top-right 22×22 circle (margin 8): **selected** = white disc with a black (`#141414`) check (11 pt);
    **not** = `#33000000` disc with a 1.5 pt `#CCFFFFFF` ring;
  - selected tile also gets a 2 pt **white border** around the whole tile;
  - click toggles. Accessibility label = the interest name.
  - Initial selection = the current settings (defaults: nature, mountains, ocean, space, architecture).
- Under the grid: hint (13 muted) on the left; "Select all" and "Clear" pill buttons (height 32) on the right.
  Hint: 0 → "Pick at least one to continue." / 1 → "1 picked. Pick a few more for variety." / n → "{n} picked. Nice mix."
- **Next is disabled while nothing is picked.**

**Step 2 — How often** (spacing 18)
- Title "How often should it change?" / muted "Most people like one new photo a day. You can change this any time."
- Five cards (spacing 8), each `surface`, radius 16, button padding 18×12; left: label (semibold) + description
  (13 muted); right: a radio mark 22×22 (chosen: `ink` disc with a `paper` check 11 pt; else a 1.5 pt `muted` ring).
  Chosen card gets a 2 pt `ink` border. Exactly one is always chosen (clicking the chosen one keeps it).

  | Label | Minutes | Description |
  |---|---|---|
  | Once a day | 0 | A new photo every day, at the time you pick. |
  | Every hour | 60 | Something new all day long. |
  | Every 3 hours | 180 | Morning, afternoon and evening. |
  | Every 6 hours | 360 | A few times a day. |
  | Every 12 hours | 720 | Day and night. |

- When **"Once a day"** is chosen its card grows a second row (margin 18/0/18/14): clock icon (15, muted),
  "Every day at", and a time picker (24 h, **5-minute steps**) on the right. Initial = settings' daily time.
- Initial choice = the current `intervalMinutes` (else Once a day).

**Step 3 — Make it yours** (spacing 10). **Mac adaptation** (Windows had accent color, lock screen, start with
Windows; the first two don't exist on Mac):
- Title "Make it yours" / muted "A few choices about how Oriel behaves. All can be changed later in Settings."
- Cards (padding 18×14, `surface`, radius 16, icon tile 40×40 radius 12 with `paper` background, title semibold,
  13 pt muted description, a switch on the right):
  - `moon.stars` — "Match the time of day" / "Darker photos in the evening, brighter ones in the morning." → `matchTimeOfDay`
  - `power` — "Open at login" / "Oriel waits quietly in the menu bar so your schedule keeps running." → `startAtLogin`
  - (only with 2+ displays) `display.2` — "A photo for each display" / "Give each display its own photo instead of the same one everywhere." → `photoPerMonitor`

**Step 4 — Done** (spacing 22)
- 64×64 `ink` circle with a 26 pt `paper` check, left-aligned.
- Title "You're all set" / muted "Here's what Oriel will do. Let's find your first wallpaper."
- Summary card (`surface`, radius 16, padding 18×6): three rows (padding 10 vertical, columns: icon 15 `muted` |
  label 110 wide `muted` | value), separated by 1 pt `line` dividers:
  - `heart` "Interests" → picked labels; ≤ 3: "Nature, Mountains, Ocean"; more: "Nature, Mountains, Ocean and 2 more"
  - `clock` "New photo" → "Every day at 09:00" or the chosen label ("Every 3 hours")
  - `display` "Applied to" → **Mac:** "All displays" or "Each display its own photo"; add ", matching the time of day" when on
- Tip (13 muted): with a shortcut: "Tip: press ⌃⌥W to change your wallpaper from anywhere. Oriel lives in your menu bar."
  without: "Oriel lives in your menu bar: open it from there any time."

**Footer** (all buttons height **40**): left: "Back" (plain pill, min width 96, chevron-left 12 pt) on steps 1–3.
Right: on steps 0–3 the primary "Get started" (step 0) / "Next" (min width 140, chevron-right 12 pt). On step 4: plain
"Just take me in" and primary with an `arrow.clockwise` icon: "Set my first wallpaper", or "See the shared
wallpaper" when a shared photo is waiting (§36).

**Finish(changeNow):** save in one update: built-in interests on/off as picked (custom ones untouched),
`intervalMinutes`, `dailyTime` ("HH:mm"), the step-3 choices, `onboardingDone = true`. Hide the tour. If changeNow
and **no shared photo is waiting**, start a Change now (Home shows its progress). A waiting shared photo *is* the
first wallpaper: its card shows next.
**Skip:** save `onboardingDone = true` only; hide the tour.

---

## 30. Home

Reference: `Pages/HomePage.xaml(.cs)`, `ViewModels/HomeViewModel.cs`.

Scrollable column (max 960, page margins), spacing 16, top to bottom:

### 30.1 Banner
The InfoBar (closable) for results and errors (§30.6).

### 30.2 The preview
- Height **440**, `surface` background, radius **20**, clips its content.
- Empty state (no history): centered logo 48×48 at 25% opacity + muted "No wallpaper yet. Press Change now."
- The current wallpaper = newest history entry, loaded from its **local file** decoded at ~1200 px wide (fallback:
  its thumbnail URL), aspect-fill. Reveal M1 when it changes. Only reload the image when the entry id or file
  changed (one decode per change). Accessibility label "Current wallpaper".
- A transparent layer on top for the star bursts (doesn't take clicks).

### 30.3 Double-click to favorite: the star burst ("TikTok effect")

Exactly as Windows (`HomePage.Preview_PointerPressed`, `BurstStar`):
- Every mouse-down on the preview records its time. A mouse-down **within 350 ms of the previous one** is a "like"
  (the first click of a pair does nothing). Keep clicking quickly: **every** further quick click is another like, a
  star each time, as many as you like.
- Only when there is a current wallpaper.
- A like **sets favorite = true** (never un-favorites); it writes to history only if it wasn't already a favorite.
  The Favorite toggle (§30.4) updates and favorites sync (§18.3) fires.
- A star spawns **centered on the click point**:
  - filled star (SF Symbol `star.fill`), size **72 pt**, color `#F5B301`;
  - random initial rotation in **−15…15°** (integer), animates to 0 over the whole 950 ms;
  - scale keyframes: 0 ms **0.3** → 170 ms **1.25** → 950 ms **2.0**;
  - vertical move: stays at 0 until **110 ms**, then rises to **−150 pt** at 950 ms;
  - opacity: 1 until **522 ms** (55%), then fades to 0 at 950 ms;
  - each keyframe segment uses cubic ease-out; scale/rotation around the star's center;
  - removed from the layer when done. Several stars can be in flight at once.
- Implement as a SwiftUI overlay with `TimelineView`/keyframe animations or a CALayer per star; must stay smooth.

### 30.4 Credit and actions row
Two columns, spacing 16.
- Left (only with a current wallpaper), margin-left 4: "Photo by" **[Photographer]** "on" **[Unsplash]** where the two
  are link buttons (semibold, padding 5×2) opening the photographer URL and the photo page URL.
- Right, spacing 8, vertically centered:
  1. **Share** (icon button 36, `square.and.arrow.up`; becomes `checkmark` for 2 s after a copy). Enabled only when
     the photo can be shared (Unsplash). Tooltip "Share this wallpaper. Whoever opens the link can set it in one
     click." Action: make the link (§19), count `sharesSent`, open `NSSharingServicePicker` anchored to the button
     with the text "A wallpaper I like. Open this link to set it on your computer in one click:" and the URL (title "A
     wallpaper for you"). If the picker can't open, copy the link to the pasteboard and show the check. Errors →
     banner "Couldn't share" + message (warning).
  2. **Lock** toggle (36 circle): unlocked = `lock.open`, locked = `lock` (filled look). Checked = **paused**. Tooltip
     "Lock to keep this wallpaper. Unlock to let it change." Saves `paused` (synced). The checked state keeps the
     `surface` background (no accent fill).
  3. **Favorite** toggle (36 circle): `star` outline, or `star.fill` in `#F5B301` when on; checked keeps the `surface`
     background. Enabled with a current wallpaper. Tooltip "Favorite: keep this one forever".
  4. **Change now** primary pill, height 36: leading item = spinner (14, paper color) while busy / `arrow.clockwise`
     (14) when idle and allowed / `clock` (14) when rate-limited; label (M3 swap):
     - busy → the phase words ("Finding a photo…", "Downloading…", "Changing…", "Syncing…", "Receiving…")
     - limited → "Try after {N} min" (disabled)
     - else "Change now"
     Disabled while busy or limited. Accessibility "Change wallpaper now".

### 30.5 Wallpaper of the day card
Shown once today's photo is known. Margin-top 8, padding 10/10/16/10, `surface`, radius 16, columns spacing 16:
- 128×80 thumbnail, radius 12, `line` placeholder, M1 reveal.
- "Wallpaper of the day" (semibold); under it 12 pt: muted "Photo by" + link [By] + muted "on Unsplash".
- Pill button on the right: "Set as wallpaper" / while working: the phase words (start "Finding it…") / when it's
  already the current wallpaper: "On your desktop ✓" (disabled).

### 30.6 Behavior
- **Phase follow:** Home listens to *every* phase from the wallpaper service (this button, the schedule, the menu
  bar, the shortcut, another device). Any phase except Done → busy + the words; Done → idle (Home refreshes from the
  `applied` event, not here).
- **Change now** (`changeAsync`): if busy → `.busy`. Busy = true, close banner, `apply(force: true)` off the main
  thread, show the result, then (unless still syncing) not busy; refresh.
- **Results:** failed → title "Couldn't change the wallpaper", message "{error} Your current wallpaper was kept."
  (error severity for key problems (missingKey/unauthorized), else warning). Busy → "Hang on" / "Another change is
  already in progress." (info). RateLimited → "Slow down a little" / the message (info).
- **Refresh:** reload paused state, the limit (below), today-current flag, the newest entry (favorite flag,
  photographer, links, shareability, preview). If `state.lastError` and not busy → warning "Last change didn't work"
  with "{error} Retrying automatically at HH:mm." (when `nextRetryAt` is in the future) else just the error.
  Otherwise close a non-success banner.
- **Limit countdown:** `limitMinutes = minutesUntil(manualChangeAvailableAt)`; while limited a 15 s timer
  re-checks so the button comes back by itself.
- Refresh on: window activation, settings changed, applied, follow changed, sign-in.

---

## 31. Search

Reference: `Pages/CollectionsPage.xaml(.cs)`, `ViewModels/CollectionsViewModel.cs`, `ViewModels/SearchIdeas.cs`.

Column (max 960, margins 40/32/40/40):

1. **Search bar**: height 52, radius 26, `surface`, padding 18 left / 6 right, spacing 10:
   magnifier (16, `muted`) | borderless text field (15 pt, placeholder "Search wallpapers, like northern lights or
   tokyo night", no own clear button, no focus ring) | clear ✕ icon button (only when there's text; tooltip "Clear") |
   primary pill "Search" (height 40, radius 20, padding 22, spinner while searching). **Return** submits.
   Search disabled while searching or text is blank.
2. Banner (margin-top 12).
3. **Before the first search** (start state):
   - "Need an idea?" card: padding 20, `surface`, radius 20, spacing 14; header row: "Need an idea?" (semibold) and a
     "Shuffle" pill (`shuffle` icon) on the right; then **8 random ideas** (Appendix B) as wrapping pills
     (spacing 8), each with `paper` background, a small `muted` magnifier (12) and the words. Clicking one searches it.
   - **Interests row**: full-width button, min height 68, margin-top 22, padding 20/14/16/14, radius 16: `heart`
     (18 muted) | "Interests" (semibold) + summary (12 muted, wraps): "{on} of {all} on. Each new wallpaper comes from
     one of them." | chevron right (12 muted). Opens the Interests page (slides in from the right).
   - Ideas reshuffle on every visit to Search (not when coming back from Interests).
4. **After a search** (results state; the start content is hidden):
   - Header: "Results for “{query}”" (section header style) and, on the right, "Add to Interests" pill (`plus` icon,
     tooltip "Add this search to your Interests, so the daily wallpaper can come from it"), only when the query
     isn't already an interest.
   - No results card (padding 24×40, `surface`, radius 20, centered): magnifier 28 muted, "No photos found"
     (semibold), muted "Try other or fewer words. Only photos big enough for your screen are shown." (max width 360).
   - Results grid: adaptive columns, **min item width 280**, spacing 14. Card: height 210, radius 16, `line`
     placeholder, photo aspect-fill (thumbnail = Unsplash `small`, decoded ~360 pt, M1 reveal), tooltip "{w} × {h}",
     accessibility = description. Bottom scrim area (padding 14/44/10/10): photographer link (white, 12 pt, tooltip
     "Photographer on Unsplash") on the left; on the right a light pill (§25.1 on-photo light pill, padding 14×5,
     13 pt): spinner 12 + label: "Set as wallpaper" / phase words (start "Downloading…") / "Wallpaper set ✓" (only
     the last set one shows ✓).
   - "Show more" pill centered (margin-top 16) when more pages exist.

**Logic:**
- Searching the same words while results are on screen does nothing (no new request).
- Each load fetches the next page, and up to **3 pages** in total until at least **6** photos passed the size filter.
- At least **1.5 s** between two requests to Unsplash (mashing Enter can't burn the hourly limit).
- Clearing (✕) bumps a version counter so in-flight loads are discarded, empties everything and returns to the start.
- Errors → banner "Couldn't search" + message (error for key problems, else warning).
- **Set:** only one at a time; runs `applyPhoto(photo, topic: query)` with phase words on that card; failure →
  banner "Couldn't set the wallpaper" + error.
- **Add to Interests:** lowercase trimmed query; if it exists turn it on, else add a custom interest (on) and look up
  its cover (§32).
- Window closed → drop results and thumbnails.

---

## 32. Interests

Reference: `Pages/InterestsPage.xaml(.cs)`, `ViewModels/CollectionsViewModel.cs` (`TopicViewModel`).

Sub-page of Search. Column (max 960, margins 40/32/40/40), spacing 20:
- Header: back icon button (chevron-left 14, tooltip "Back") | "Interests" (page title, no margin) + muted summary |
  "Select all" and "Clear" pills (they affect the **built-in** ones only).
- Warning banner (not closable) when nothing is on: "Nothing is on, so Oriel uses general landscape photos."
- Built-in tiles grid: adaptive, **min item width 190, height 136**, spacing 12. Tile = same design as onboarding
  (§29 step 1) but: radius 16, label margin 14/0/14/12, check circle 24 (check 12 pt) with margin 10, 2 pt white
  border when on, tooltip on the photo "Photo by {name} on Unsplash". Hover zoom M7. Click toggles on/off (saves
  immediately; synced).
- "Your own" section (margin-top 8, spacing 12): "Your own" (15 semibold), muted "Search for anything, then press Add
  to Interests to make it one of yours.", then the custom tiles grid (only when there are some). Custom tiles:
  - photo = kept `coverUrl`, else the thumbnail of the last wallpaper with that topic, else none;
  - without a photo: centered logo (30, 35% opacity, the dark-variant logo) and 12 pt `#99FFFFFF` text: "Finding a
    photo…" while looking it up, else "Photo comes soon";
  - a remove button top-left: 28×28 circle, `#66000000` background, white ✕ (10 pt), margin 8, tooltip "Remove".
- On appear: for each custom interest without a cover, once per app run, call `findCover(query)` one at a time;
  save the URL; if nil (no requests to spare) or offline, allow a retry next visit.
- Saving topics keeps `coverUrl` only for custom ones.

---

## 33. History and Favorites

Reference: `Pages/HistoryPage.xaml(.cs)`, `ViewModels/HistoryViewModel.cs`.

One page, two modes: **History** (everything) and **Favorites** (starred only).
- Header: page title ("History" / "Favorites"); right side, spacing 10: while "Set again" works: spinner 14 + the
  phase words (muted); a **segmented pill** (padding 3, `surface`, radius 12; selected item `ink` background radius 9
  with `paper` icon; others `muted`): grid (`square.grid.2x2`, tooltip "Grid") / list (`list.bullet`, tooltip "List").
  Grid is the default; the choice is shared by both modes for the app run.
- Info banner (margin-bottom 12) for errors ("Couldn't re-apply: {message}").
- Empty state (margin-top 80, centered): logo 48 at 25% + muted "Nothing here yet." / "No favorites yet. Star a photo
  to keep it here."
- **Grid**: adaptive, min item width **300**, spacing 12. Card height 210, radius 16, `line` placeholder:
  - thumbnail (local file decoded ~320 px, else URL), aspect-fill, M1;
  - favorites show a small gold `star.fill` (16) top-right (margin 12) at all times;
  - bottom scrim area (padding 14/44/10/10) using the on-photo styles: details (left): photographer link (semibold,
    white, tooltip "Photographer on Unsplash") and a 12 pt `#CCFFFFFF` line "{Tue, 14 Oct 2026} · {topic}"
    (truncates); actions (right, spacing 4, hidden until hover/focus, M10): **Set again** (`arrow.uturn.backward`,
    tooltip "Set as wallpaper again", enabled if the file exists or it's an Unsplash photo), **Favorite** toggle
    (star / gold star.fill, tooltip "Favorite: never auto-delete"), **Show in Finder** (`folder`, tooltip "Show in
    Finder"), **Delete** (`trash`, tooltip "Delete"). When actions show, the details shrink (right margin = actions
    width + 8) so text never runs under the buttons.
- **List**: rows spacing 8; row padding 8, `surface`, radius 12, columns spacing 14: thumbnail 112×64 radius 8 |
  details (photographer link semibold, 12 pt muted "{date} · {topic}") | same actions (spacing 2, normal styles).
- Date format: `EEE, d MMM yyyy` ("Tue, 14 Oct 2026").
- **Loading:** build the list instantly from the DB, reuse item view-models by id between visits (same cards and
  thumbnails appear at once), then load missing thumbnails one by one; a newer load (switching modes fast) stops an
  older one. Replace the whole list at once, never mutate item-by-item during layout. If nothing changed, leave the
  grid untouched (no re-reveal).
- Unstarring in Favorites mode removes the card at once.
- **Set again:** one at a time; phase words in the header; errors → banner.
- **Delete:** confirm dialog — title "Delete this wallpaper?", text "The file will be deleted, and Oriel won't pick
  this photo again." (favorite: "This is a favorite. The file will be deleted, and Oriel won't pick this photo
  again."), buttons **Delete** (destructive) / **Cancel** (default). Then delete file + mark deleted; remove the card.
- **Show in Finder:** `NSWorkspace.shared.activateFileViewerSelecting([url])`; file gone → open its folder.
- Window closed → drop items and thumbnails.

---
## 34. Friends

Reference: `Pages/FriendsPage.xaml(.cs)`, `ViewModels/FriendsViewModel.cs`.

Column (max 960, page margins), spacing 16:
1. Title "Friends".
2. **Following card** (only while following): height 260, `#1C1C1C`, radius 20, always dark styling.
   - Until the friend's wallpaper is here: an 80×80 circle (`#33FFFFFF`, margin-bottom 50, centered) with the
     friend's initial (32 semibold white), covered by their Google picture when they have one (https only).
   - Once here: the newest history entry with topic "from {FriendName}", local file decoded ~900 px, aspect-fill,
     with the scrim.
   - Bottom (padding 24×20): "Following {Name}" (24 semibold white, truncates) and under it (white, 80% opacity):
     "Photo by {photographer}" or "Their wallpaper arrives in a moment."; on the right an "Unfollow" pill (height 40).
   - Unfollow asks: title "Stop following {Name}?", text "Oriel goes back to your own wallpapers.", buttons
     **Unfollow** / **Cancel** (default).
3. While a follow link is being followed: spinner 18 + muted "Following your friend…".
4. **One list card** (`surface`, radius 20). Rows: padding 20×16, columns spacing 16: icon tile 40×40 radius 12 with
   `paper` background (16 pt icon) | title (semibold) + 13 pt muted detail | buttons. Rows after the first have a
   1 pt `line` divider at the top inset 76 left / 20 right.
   - **Share my wallpaper** (`square.and.arrow.up`). Detail: not sharing → "Off. Friends get every wallpaper you
     set."; sharing → "On." / "On. No one follows yet." / "On. 1 person follows you." / "On. {n} people follow you."
     Buttons: "Turn off" pill (only while sharing; height 36) and primary "Share" (height 36, min width 96; "Getting
     your link…" while working; "Link copied" for 2 s after the fallback copy). Share opens the share picker with
     title "Follow my wallpaper", text "Follow my wallpaper on Oriel: whenever I change mine, yours changes too." and
     the follow link; fallback: copy.
     Turn off asks: "Turn off sharing?" / "Your link stops working." + " The 1 person following you stops getting
     your wallpaper." or " The {n} people following you stop getting your wallpaper." / **Turn off** / **Cancel**.
   - **Follow a friend** (`person.badge.plus`), only while **not** following: "Use a friend's wallpaper." / "Enter
     code" pill (height 36, min width 96).
   - **Followers** (`person.2`), only when someone follows: "1 person" / "{n} people" / "See all" pill.
5. Warning banner (closable) for problems: a sharing/following error, a follow-link error (shown once), or "X stopped
   sharing their wallpaper." (for as long as it's true).

**Enter code sheet:** title "Follow a friend"; a text field height 44, monospaced 18 pt with wide letter spacing,
no spell check, max 9 chars, placeholder "K7MQ-2XPA", auto-formatted as typed (§19 `formatInput`, caret at end);
error text in red under it; buttons **Follow** (default, enabled only when the code is valid and not busy) /
**Cancel**. Follow keeps the sheet open showing the error until it works; on success the sheet closes and the
Following card appears with the friend's photo.

**Followers sheet:** title "Followers", button "Done". Empty: muted "No one yet. Share your wallpaper and friends
who follow show up here." List (max height 320, scroll), rows padding 6, spacing 12: 36×36 `ink` circle with the
initial in `paper` semibold | name (semibold, truncates) + "Since Oct 14" (12 muted, `MMM d`) | "Remove" pill: first
click → "Remove?" (resets others), second click removes. Loads the latest list when opened.

Follower count loads on appear when sharing. The page reloads on follow changes.

---

## 35. Settings

Reference: `Pages/SettingsPage.xaml(.cs)`, `ViewModels/SettingsViewModel.cs`, `Controls/SettingRow.*`.

Column (max 960, page margins), row spacing 6. Title "Settings". Every change saves immediately (`update {}`);
synced ones go to the account (§18.2).

**Account** (section header "Account", margin 2/4/0/8)
- Card (min height 64, padding 20/12/16/12, `surface`, radius 16): 40×40 circle with the Oriel logo (22) on `paper`,
  covered by the Google profile picture (https, decoded ~80 px) once it loads (a failed photo shows the logo) |
  name (or "Signed in") and email (12 muted) | "Manage account" (opens `https://oriel.akbarjon.uz/me`) and
  "Sign out" buttons.

**Schedule**
- `photo.on.rectangle` "Change wallpaper" — "Once a day, or as a slideshow that keeps changing through the day." —
  picker (min width 150): Once a day / Every hour / Every 3 hours / Every 6 hours / Every 12 hours (0/60/180/360/720).
  An unknown synced value shows as Once a day.
- `photo.stack` "Pick from" — "Favorites cycles through the photos you starred in History. With none, new photos are
  used." — picker: New photos / My favorites (`favoritesOnly`).
- `clock` "Daily change time" — "A new wallpaper once a day at this time." — 24 h time picker with 5-minute steps.
  **Only in Once a day mode.**
- `person.crop.circle` "Change at login" — "If today's wallpaper isn't set yet when you log in, change it right
  away." — switch (`applyAtLogin`). Only in Once a day mode.
- `keyboard` "Next wallpaper shortcut" — description = the registration error or empty — a shortcut recorder field
  (width 170, placeholder "Click, then press keys", shows e.g. "⌃⌥W") + "Clear" button (§38).

**Display**
- `rectangle.expand.vertical` "Fit" — "How the photo fits your screen. Span stretches one photo across all displays."
  — picker (min width 110): Fill / Fit / Stretch / Center / Span. Takes effect at the next change.
- `moon.stars` "Match the time of day" — "Darker photos in the evening and in dark mode, brighter ones in the
  morning." — switch.
- `display.2` "A photo for each display" — "Give each display its own photo instead of the same one everywhere." —
  switch. **Only with more than one display.**
- (Windows' "Apply to" and "Match accent color" rows are not on Mac.)

**Quality**
- `hand.thumbsup` "Skip photos with few likes" — "Prefer photos other people liked on Unsplash." — switch.
- `slider.horizontal.3` "Minimum likes" (no description) — slider width 200, 0…1000, step 50, tick marks every 50,
  with the value (min width 36) beside it. Only when skipping is on. Stored rounded to 50.

**Storage**
- Banner (radius 12) for folder results.
- `folder` "Wallpaper folder" — description = the full path — "Open" (opens it in Finder, creating it if needed) and
  "Change" (spinner 14 while moving; tooltip "Pick an empty folder; your photos move there.") (§22).

**App**
- `power` "Open at login" — "Start in the menu bar when you log in." — switch. Turning it **off** asks first:
  title "Turn off Open at login?", text "Oriel won't open when you log in, so the menu bar icon and your shortcut
  won't work until you open it yourself. Scheduled wallpaper changes will still run.", **Turn off** / **Keep on**
  (default). Cancel puts the switch back on.
- `arrow.triangle.2.circlepath` "Updates" — description "Version {x.y.z} beta · Oriel checks for updates once a day
  and installs them in the background." (no " beta" once out of beta) — "Check for updates" button (Sparkle).

**Help**
- `bubble.left` "Contact support" — "Questions, bugs or ideas? Message the Oriel support bot on Telegram." — "Open
  Telegram" button → `https://t.me/oriel_support_bot`.

---

## 36. Shared photo card

Reference: `Controls/SharedPhotoView.xaml`, `ViewModels/SharedPhotoViewModel.cs`.

A dimmed layer (`#99000000`) over the app with one card in the middle: max width 520, margin 24, `paper`, 1 pt
`line` border, radius 20, clips.
- **Opening** (`open(photoId, from)`): a newer link replaces an older one (version counter). Heading "{From} shared
  a wallpaper with you" or "Someone shared a wallpaper with you". Count `sharesOpened`. Look up the photo
  (`getSharedPhoto`).
- **Loading:** padding 40×56, spinner 32, muted "Getting the photo…".
- **Photo:** image height 260 (thumbnail decoded ~720, aspect-fill, `surface` placeholder); then padding
  28/22/28/26, spacing 18: heading (page title style, wraps) — after setting: "It's your wallpaper now"; muted
  "Photo by **{name}** on Unsplash"; a warning banner if setting failed; buttons right-aligned, spacing 8, height 38:
  "Not now" pill and primary "Set as wallpaper" (min width 150; spinner + phase words, start "Downloading…"); after
  success only a primary "Done" (min width 120).
- **Failed to load:** padding 32×36, spacing 16: title "Couldn't open that photo", the error (404 → "That photo
  isn't available any more."), "Close" pill right-aligned.
- Set = `applyPhoto(photo, topic: "shared")`; success → done state + count `sharesSet`; failure → banner with
  "Couldn't set the wallpaper. Try again." or the error.
- It waits behind the sign-in screen and the onboarding tour, then shows over the app.

---

## 37. Menu bar icon

Reference: `Services/TrayIconService.cs`.

`NSStatusItem` with a **template** image of the Oriel mark (monochrome, 18 pt high, adapts to the menu bar). Tooltip
"Oriel", or "Oriel (paused)" when paused. Clicking shows the menu (Mac convention; Windows left-click opened the
window, which the first item covers):

1. **Next wallpaper** — change now (§ below)
2. **Open Oriel**
3. **Pause daily changes** — checkmark when paused; toggles `paused`
4. separator
5. **Quit Oriel**

Refresh the checkmark/tooltip when settings change (only when `paused` actually changed).

**Next wallpaper / shortcut** (`ChangeFromTrayAsync`): not signed in → open the window. Otherwise, if Home isn't
already busy and (the shortcut was used **or** the window isn't visible) show the **change popup** (§39) and feed it
the phases; run Home's `changeAsync()`; dismiss the popup at the end. Any popup failure must never stop the change.

---

## 38. Global shortcut

Reference: `Services/HotkeyService.cs`, `SettingsPage.Shortcut_KeyDown`.

- Default **Control-Option-W**, stored as `"Ctrl+Alt+W"`. Mac names in storage: `Ctrl`, `Alt` (Option), `Shift`,
  `Cmd`; shown with symbols in this order: ⌃ ⌥ ⇧ ⌘ then the key ("⌃⌥W").
- Register with Carbon `RegisterEventHotKey` (works while the window is closed, no Accessibility permission). One
  shortcut at a time. Failure → "{shortcut} is already used by another app. Pick a different shortcut." (shown as the
  row's description).
- Allowed keys: A–Z, 0–9, F1–F20, Space, Page Up/Down, Home, End, arrows, Forward Delete. A shortcut needs at least
  one of Control, Option or Command (Shift alone isn't enough), so plain typing never triggers it.
- Recorder: focus the field ("Click, then press keys"), the next valid combination is saved and registered at once;
  "Clear" removes it (empty string = none).
- Pressing it runs the menu bar's "Next wallpaper" **with the popup always shown**. Log "Shortcut pressed".

---

## 39. The change popup (the pill)

Reference: `windows/src/Oriel.App/ChangePopup.cs`.

A small dark pill that rises from the bottom of the screen while a change happens from the menu bar or the shortcut
(like the "Desktop 4" label when switching desktops on Windows).

- **Window:** a borderless, non-activating `NSPanel` (`.nonactivatingPanel`, `.borderless`), `level = .statusBar`,
  `collectionBehavior = [.canJoinAllSpaces, .transient, .ignoresCycle]`, `hidesOnDeactivate = false`, never key,
  not in Mission Control / Cmd+Tab, ignores the mouse.
- **Look:** height **40**, fully rounded (corner radius 20); background = an `NSVisualEffectView` (material
  `.hudWindow`, `.behindWindow`, state `.active`, dark appearance) with a veil of `#141414` at **35%** opacity over it
  so white text reads; no border/shadow beyond the system's. Content: horizontal, spacing **10**, padding **18** left
  and right, centered: a small white spinner (16) and the word (13 pt, white). The pill's width is fixed to fit the
  **longest** phase word, so it never changes size between words.
- **Position:** horizontally centered on the main screen; resting y = `visibleFrame.minY + 20` (20 pt above the Dock
  or the bottom edge); hidden y = just below `visibleFrame.minY` (slides up from under the Dock area). Mac can't put it
  behind the Dock; also fade alpha 0→1 while sliding in so it doesn't overlap the Dock visibly.
- **Show:** order front without activating, slide from hidden to rest over **240 ms**, ease-out cubic
  (`1 − (1 − t)³`). First word: "Finding a photo…".
- **Words:** on each phase (except Done) switch the word: fade out + move up **8** over **120 ms** ease-in, then set the
  text, start 8 below, fade in + move to 0 over **120 ms** ease-out.
- **Dismiss:** stay visible at least **700 ms** in total, then slide back down over **240 ms**, ease-in cubic (`t³`),
  then close.

---

## 40. Progress words (phases)

Reference: `ApplyPhase`, `PhaseText`, `PhaseWatch` in `WallpaperService.cs`.

| Phase | Words |
|---|---|
| finding | Finding a photo… |
| downloading | Downloading… |
| changing | Changing… |
| syncing | Syncing… |
| receiving | Receiving… |
| done | (nothing) |

Shown on Home's button, the popup, Search result buttons, the Today button, the shared card button, History's header.

**PhaseWatch** (for a change started from a button: Search, Today, shared photo, History): subscribe to phases,
report each word to that button, run the change; if it applied, wait for Done (max **20 s**) so the button doesn't
say "set" while still syncing; finally unsubscribe and report nil. Always update UI on the main actor.

---

## 41. Command line

Reference: `windows/src/Oriel.App/Cli.cs`.

The app executable doubles as a CLI (checked before `NSApplicationMain`):
`/Applications/Oriel.app/Contents/MacOS/Oriel --status`

| Flag | What it does |
|---|---|
| `--apply-today` | Change the wallpaper if the current tick's isn't set yet (launchd runs this). Lower priority. |
| `--change-now` | Change it right now (force; the Change-now limit applies) |
| `--status` | Print settings, displays, last run |
| `--help` | Print the help |
| `--background` | Start the app in the menu bar without the window (not a CLI command) |

- `--apply-today` / `--change-now` run `ScheduledRun.run(force)`: apply; success → retention cleanup; a transient
  failure with a `nextRetryAt` → schedule the launchd retry (§15). In a CLI run, prefetch is off and after-change
  work + publishing are awaited before exiting. **Test that `NSWorkspace.setDesktopImageURL` works from a launchd
  agent run in the user's GUI session** (it should; if it requires `NSApplication.shared`, create it without a window).
- Output and exit codes:
  - applied → "Applied '{topic}' photo by {name}: {file}" per entry → **0**
  - already set → "Today's wallpaper is already set." → 0
  - paused → "Oriel is paused." → 0
  - busy / rate limited → the message → 0
  - not signed in → "Sign in to Oriel first: open the app and sign in." → **3**
  - failed → "Failed: {error} (current wallpaper kept)" → **3** for missingKey/unauthorized, else **1**
  - `--help` → the help text → 0 (exit **2** is reserved for bad usage)
- `--status` prints:
  ```
  Built-in key: true
  Daily time:   09:00   at login: true   paused: false
  Topics:       nature, mountains, ocean, space, architecture
  Last applied: 2026-10-14
  Display:      3024x1964 primary
  Wallpaper:    /Users/…/Pictures/Oriel/2026-10-14_unsplash_….jpg
  Last error:   …  (next retry 10:05)
  ```
- Help text:
  ```
  Oriel - daily wallpaper
    --apply-today   change wallpaper if today's isn't set yet (used by launchd)
    --change-now    change wallpaper right now
    --status        show current settings and last run
    --background    start the app in the menu bar
  ```
- A crash in a CLI run is logged ("Headless run crashed") and prints "Error: {message}" → 1.

---

## 42. Updates (Sparkle)

Windows uses Velopack (checks a minute after start and daily, downloads in the background, installs on restart/quit).
**Mac: Sparkle 2.**
- Feed: `https://github.com/oriel-releases/macos/releases/latest/download/appcast.xml` (`SUFeedURL` in Info.plist).
- EdDSA keys from Sparkle's `generate_keys`: the **public** key in Info.plist (`SUPublicEDKey`); the **private** key
  is kept by Akbar (Keychain on the release machine + an offline backup). **Losing it means existing installs can
  never update again** (like the Android release key).
- Automatic checks on, interval 86400 s, automatic download on (`SUEnableAutomaticChecks`, `SUAutomaticallyUpdate`).
- `SPUStandardUpdaterController`; "Check for Updates…" in the app menu and the Settings button.
- Updates work only in the installed app (not when run from Xcode/`build/`): in that case the Settings row says
  "Version {x} beta · Updates work in the installed app."

---

## 43. Logging

Reference: `Logging/FileLogger.cs`.

Daily file `~/Library/Logs/Oriel/oriel-YYYY-MM-DD.log`, level ≥ info, one line per event:
`HH:mm:ss.SSS [INFO] [pid] Category: message | ErrorType: error message`. Level tags: `INFO`, `WARN`, `ERRO`, `DEBU`,
`TRAC`, `CRIT` (first 4 letters, upper case). Category = the short type name (`WallpaperService`). Logging never
throws. **Never pass secrets to log calls.** Keep the Windows log messages (they're quoted throughout this document)
so logs read the same on every platform.

---

## 44. Performance and battery

- **Idle is nothing.** No polling loops. The scheduler sleeps until the next due moment (max 10 min). The SSE
  connection waits in the kernel. The hotkey and status item are event-driven.
- Let **App Nap** work while the window is closed. Exception: hold
  `ProcessInfo.processInfo.beginActivity(options: .userInitiatedAllowingIdleSystemSleep, reason: "Changing the wallpaper")`
  only while a change runs, and while waiting the **last 2 minutes** before a due tick (App Nap could otherwise delay
  the timer noticeably).
- Closing the window destroys it: release the Home preview, History items and Search results (they're rebuilt when
  the window opens).
- Never decode full-size photos for the UI: `CGImageSourceCreateThumbnailAtIndex` with `kCGImageSourceThumbnailMaxPixelSize`
  (1200 Home, 900 Friends card, 720 shared card, 360 search, 320 history) and `kCGImageSourceCreateThumbnailFromImageAlways`.
  Load local files without keeping them open.
- Background work at utility QoS. CLI runs at low priority (`setpriority`/QoS `.utility`).
- One Home preview decode per change (refresh only when the entry id or file changed).

---

## 45. Packaging and releasing

**Free constraint:** no paid Apple Developer Program. So:
- Builds are **ad-hoc signed** (`codesign --force --deep -s -`), required on Apple Silicon. Not notarized.
- First launch: Gatekeeper blocks it. On macOS 14 and older: right-click the app → Open → Open. On macOS 15+:
  try to open it, then **System Settings → Privacy & Security → "Open Anyway"**. The website's download page and the
  README must explain this (Akbar adds it to the website, §46).
- (Optional, still free: a self-signed "Oriel Code Signing" certificate made in Keychain Access gives every release a
  stable signature. Not required for 1.0.)

**Release artifacts** on `https://github.com/oriel-releases/macos` (a public, releases-only repo, like the others,
so the updater reads it without a token), tag `v{version}`, a normal (not pre-) release:
- `Oriel.dmg` — drag-to-Applications disk image (what the website links: `releases/latest/download/Oriel.dmg`)
- `Oriel.zip` — the app zipped with `ditto -c -k --keepParent` (Sparkle downloads this)
- `appcast.xml` — made by Sparkle's `generate_appcast` / `sign_update` with the EdDSA private key

**`macos/scripts/release.sh`** (mirror `windows/scripts/release.ps1`): read the version from `project.yml`
(`MARKETING_VERSION`; `CURRENT_PROJECT_VERSION` = an increasing build number), `xcodegen generate`,
`xcodebuild -configuration Release -arch arm64 -arch x86_64 ONLY_ACTIVE_ARCH=NO`, ad-hoc sign, zip, dmg (`hdiutil
create -volname Oriel -srcfolder … -ov -format UDZO`), sign the zip for Sparkle, write the appcast, then (unless
`--no-upload`) create the GitHub release `v{version}` with the three files using `GITHUB_TOKEN` (Contents: write on
`oriel-releases/macos`). Fail clearly if `UNSPLASH_ACCESS_KEY` is missing.

**Releasing a new version** (add to the repo's CLAUDE.md "Workflow rules"):
1. Bump `MARKETING_VERSION` (and the build number) in `macos/project.yml`.
2. `GITHUB_TOKEN=… macos/scripts/release.sh`.
3. Set `version` in the Firestore document **`application/macos`**.
4. Update `fallbackVersions.macos` and `releasedOn` in `website/src/config/release.ts`; rebuild and redeploy the site.

---

## 46. Backend changes Akbar must make for macOS

These are outside `macos/` and must be **deployed by Akbar** (the Firebase project owner). Don't use another
platform's name as a workaround.

**Status (2026-10-14):** steps 1–5 are **done**. The rules accepting `macos` are deployed, `application/macos` exists
with `version: "1.0.0"`, and `github.com/oriel-releases/macos` exists. Steps 6–8 (website, CLAUDE.md, checks) are
still to do: list them in the PR description.

1. **`website/firestore.rules`**
   - `match /application/{app}`: allow `'macos'` in the list.
   - `validCurrent`: `d.source in ['windows', 'linux', 'android', 'extension', 'macos']`.
   - `stats`: add `'macos_new'` and `'macos_active'` to `statKeys()`, and the matching `startsAtOne(...)` /
     `plusOne(...)` checks in create and update.
2. **`website/database.rules.json`**: `source` regex → `^(windows|linux|android|extension|macos)$`.
3. Deploy: `firebase deploy --only firestore:rules` and `firebase deploy --only database`.
4. Create the Firestore document `application/macos` with field `version` (string).
5. Create the public repo `oriel-releases/macos`.
6. **Website** (`website/`): add `macos` to `AppPlatform` in `lib/latest-version.ts`, `repoUrls`, `fallbackVersions`
   and `releases` in `config/release.ts` (fileName `Oriel.dmg`, requirements "macOS 13 Ventura or newer, Apple
   silicon or Intel"); `canRunOriel` in `lib/platform.ts` → include `"mac"`; the download page's Mac tab and the
   Gatekeeper "Open Anyway" note; `/me`'s platform icons/names (`components/me/MeContent.tsx`, `lib/sessions.ts`
   `platformNames`) so a `macos` session and a `source: "macos"` wallpaper show as "Mac app".
7. **`CLAUDE.md`**: add the macOS row to the table, the release steps, and a short "macOS app" section.
8. The browser extension and Android just read `source`; check they don't reject unknown values (they display it).

---

## 47. Build order (phases) with checks

Work in this order; build, test and **run** after each phase. Don't build UI around something that doesn't work yet.

**Phase 0 — Setup**
- Check what's installed: Xcode (and command line tools), `xcodegen`, Swift version, `gh` (optional). Tell the user
  exactly what's missing. Get the Unsplash key into `macos/.env`.
- Create `macos/` with `project.yml`, the three targets, Info.plist (URL scheme), `.gitignore`, README stub.
- ✅ An empty app builds and launches; tests run.

**Phase 1 — Core logic (no UI)**
- AppPaths, models, ShareLink, settings store (+ `updateBuiltInTopics`), state, SQLite history, Unsplash provider,
  topic picker, candidate filter, mood, ScheduleRules, retry backoff, file logger.
- ✅ Port the tests (§48); all green. Especially the schedule vectors in §13.

**Phase 2 — Set a wallpaper end to end from the CLI**
- `MacWallpaperSetter` (fit modes, per display, span), `WallpaperService.apply` without sync (auth optional for this
  phase via a debug flag), `--change-now`, `--status`.
- ✅ `Oriel --change-now` really changes the desktop on every display; `--status` prints the displays.

**Phase 3 — Accounts**
- `FirebaseClient` (auth + session calls), `AuthService`, account file, deep link parsing, `oriel://` handling in the
  app delegate, sign-in screen.
- ✅ Sign in with Google and with email through the website; the session appears on https://oriel.akbarjon.uz/me;
  revoking it there signs the Mac out within 6 h (or at next start); sign out removes it.

**Phase 4 — Sync**
- SyncPublisher (+ accent sampler), PulseListener, FollowService.check, SettingsSync, FavoritesSync, SlotClaimer,
  scheduler loop, prefetch.
- ✅ (needs §46 deployed, or test against a Windows/Android device of the same account):
  change on the Mac → the other device shows it within seconds, and the reverse; change interests/schedule on one →
  the other's settings follow; star on one → starred on the other; two devices at the same tick → only one picks
  (check the logs: "takes" / "already took").

**Phase 5 — App shell**
- Menu bar icon, window lifecycle (regular/accessory), sidebar + pages skeleton, overlays (sign-in, onboarding,
  shared card, signing out), design tokens, InfoBar, SettingRow, buttons.

**Phase 6 — Screens** in this order: Onboarding, Home (with the star burst), Search, Interests, History/Favorites,
Friends, Settings, shared card.
- ✅ Walk through each screen in light and dark mode at the minimum and default window sizes.

**Phase 7 — System integration**
- Global shortcut, change popup, Open at login (+ background launch detection), launchd fallback + retry,
  ScheduleSync, Move to Applications prompt, Spaces/display re-apply.
- ✅ Quit the app; the launchd agent changes the wallpaper at the next tick. Log out/in with Open at login on → it
  starts in the menu bar only.

**Phase 8 — Updates and release**
- Sparkle, release script, DMG.
- ✅ Install 1.0.0 from the DMG, publish 1.0.1 to a test location, the app updates itself.

**Phase 9 — Polish** against §49.

Commit after each phase (or smaller), on a branch, then a PR into `main`.

---

## 48. Tests to port

From `windows/tests/Oriel.Core.Tests` (xUnit) to XCTest. Use fakes like Windows does (`Fakes.cs`: FakeProvider,
FakeSetter, FakeClock, FakeTasks/launchd, FakeStartup, temp dir AppPaths, URLProtocol-mocked HTTP).

| Windows test file | What to cover |
|---|---|
| `SchedulingTests.cs` | Every vector in §13; ScheduledRun: offline → retry scheduled at `nextRetryAt`; permanent failure / success → no retry; ScheduleSync registers once, again only when the schedule changes; pausing removes the agent; RunAtLoad only when the app doesn't open at login; forced sync re-registers |
| `WallpaperServiceTests.cs` | Never repeats; downloads before setting (failure leaves the desktop alone); filters; per-display; span size; favorites-only; Change now limit (2/h, message, minutes rounding up); prefetch take/fit rules; phases order; `.takenByOtherDevice` |
| `StorageAndScheduleTests.cs` | SQLite add/upsert/list order/soft delete/expired/updateFilePaths; JSON store corrupt-file fallback; atomic write |
| `UnsplashProviderTests.cs` | Parse (description join, UTM, small→thumb fallback), download URL params, rate-limit header handling, error mapping |
| `PersonalTests.cs` | Interests built-in update rules, mood/luminance, candidate filter |
| `AuthTests.cs` | Deep links (all forms, invalid ids, case), token refresh caching, sign-out clears |
| `ShareTests.cs` | ShareLink.photo/picture/cleanName, code normalize/format/formatInput |
| `TodayTests.cs` | Pick rule (days since 1970 local, negative-safe modulo), parse |
| `AccentPickerTests.cs` | Known colors in → expected accent out (port the cases) |
| `WallpaperFolderMoverTests.cs` | Empty-folder rule, rollback on failure, paths updated, current wallpaper re-pointed |

---

## 49. Final acceptance checklist

**Basics**
- [ ] Fresh install → window opens on the sign-in screen; Google and email sign-in both work via the website.
- [ ] Onboarding shows once after the first sign-in (empty History); every step, animation, hint and button matches §29; "Skip setup" keeps defaults; "Set my first wallpaper" changes it and Home shows the phase words.
- [ ] Existing users (History not empty) never see the tour.
- [ ] Home: preview, credit links, Share (picker + link works on another computer), Lock, Favorite, Change now with sliding words, "Try after N min" after two changes, Wallpaper of the day card.
- [ ] **Double-click the Home photo → gold star bursts exactly at the cursor, rises, grows and fades; keep clicking quickly → a star every click; the photo becomes a favorite (and syncs).**
- [ ] Search: ideas + shuffle, Return to search, results, set as wallpaper with phase words, ✓ state, Show more, Add to Interests, no-results card, 1.5 s spacing.
- [ ] Interests: tiles with photos, hover zoom, toggle, Select all/Clear, warning when none, your own with cover lookup and remove.
- [ ] History/Favorites: grid/list switch, hover actions, Set again (also for a favorite from another device: downloads it), favorite, Show in Finder, Delete with confirmation; Favorites mode removes unstarred cards.
- [ ] Friends: Share (link opens the follow page), follow with a code (auto-format, errors), Following card with the friend's photo, Unfollow, followers list with two-click remove, Turn off sharing.
- [ ] Settings: every row of §35 works and persists; synced ones reach other devices.
- [ ] Shared links: `oriel://wallpaper?id=…&from=…` opens the card; set works; works also when it arrives through sign-in.
- [ ] Follow links open on Friends and follow (also through sign-in).

**Background**
- [ ] Closing the window leaves only the menu bar icon (no Dock icon); memory drops.
- [ ] Menu bar: Next wallpaper (popup slides up with words), Open, Pause (checkmark), Quit.
- [ ] ⌃⌥W changes the wallpaper from anywhere with the popup; the shortcut can be re-recorded and cleared; conflicts show the message.
- [ ] Daily time and slideshow fire on time with the app open; with the app quit, the launchd agent does it; a missed change while asleep is caught up after wake (when allowed).
- [ ] Open at login → starts in the menu bar only.
- [ ] Offline → the current wallpaper is kept, the banner says why and when it retries (2/5/15/30/60 min).

**Sync** (two devices on one account)
- [ ] A change anywhere shows on the Mac within seconds ("Receiving…"), and the Mac's changes show elsewhere.
- [ ] Settings and interests sync both ways; schedule edits never trigger a change by themselves.
- [ ] Favorites sync both ways, including un-starring.
- [ ] Same tick on two devices → one photo for both.
- [ ] Following a friend: their changes arrive; when they stop sharing, the Mac says so.

**Quality**
- [ ] Light and dark mode both match the palette; no system blue anywhere except the severity icons in banners.
- [ ] VoiceOver reads every button (labels from Windows' AutomationProperties.Name); everything works by keyboard.
- [ ] Reduce Motion respected.
- [ ] Logs are written and contain no tokens.
- [ ] Release script produces Oriel.dmg, Oriel.zip, appcast.xml; Sparkle update works.

---
## Appendix A: every user-facing string

Use these exactly (Mac-adapted ones are marked **Mac**). `…` is the single ellipsis character.

### Window, overlays
- Window title: `Oriel (Beta)`
- Sidebar: `Home`, `Search`, `History`, `Favorites`, `Friends`, `Settings`
- `Signing out…`

### Sign-in
- `Sign in to Oriel`
- `Continue with Google` / `Continuing with Google…`
- `Use email and password instead`
- `Waiting for you to finish in the browser…`
- `Need help? Message support on Telegram`
- Errors: `Couldn't open your browser. Go to oriel.akbarjon.uz/login yourself.` · `That sign-in expired or isn't valid. Sign in again from the website.` · `Couldn't sign you in right now. Please try again.` · `That sign-in link wasn't valid. Sign in again from the website.` · `Couldn't reach Google. Check your connection.`

### Onboarding
- Header: `{n}/3` (accessibility `Step {n} of 3`), `Skip setup`
- Step 0: `Welcome to Oriel` · `A beautiful new wallpaper, picked for you. Setting up takes under a minute.` · `A new photo, on your schedule` / `Every day or every few hours, or whenever you press Change now.` · `Only what you love` / `You pick the interests. Star a favorite to keep it forever.` · `Quiet and light` / **Mac** `Oriel waits in the menu bar and only wakes up to change your wallpaper.`
- Step 1: `What do you like?` · `Pick a few. Each new wallpaper comes from one of them.` · `Pick at least one to continue.` · `1 picked. Pick a few more for variety.` · `{n} picked. Nice mix.` · `Select all` · `Clear`
- Step 2: `How often should it change?` · `Most people like one new photo a day. You can change this any time.` · `Once a day` / `A new photo every day, at the time you pick.` · `Every hour` / `Something new all day long.` · `Every 3 hours` / `Morning, afternoon and evening.` · `Every 6 hours` / `A few times a day.` · `Every 12 hours` / `Day and night.` · `Every day at`
- Step 3: `Make it yours` · `A few choices about how Oriel behaves. All can be changed later in Settings.` · **Mac** `Match the time of day` / `Darker photos in the evening, brighter ones in the morning.` · **Mac** `Open at login` / `Oriel waits quietly in the menu bar so your schedule keeps running.` · **Mac** `A photo for each display` / `Give each display its own photo instead of the same one everywhere.`
- Step 4: `You're all set` · `Here's what Oriel will do. Let's find your first wallpaper.` · `Interests` · `New photo` · `Applied to` · `Every day at {HH:mm}` · `{a}, {b}, {c} and {n} more` · **Mac** `All displays` / `Each display its own photo` (+ `, matching the time of day`) · **Mac** Tip: `Tip: press {⌃⌥W} to change your wallpaper from anywhere. Oriel lives in your menu bar.` / `Oriel lives in your menu bar: open it from there any time.`
- Footer: `Back` · `Get started` · `Next` · `Just take me in` · `Set my first wallpaper` / `See the shared wallpaper`

### Home
- `No wallpaper yet. Press Change now.`
- `Photo by` `{name}` `on` `Unsplash`
- Tooltips: `Share this wallpaper. Whoever opens the link can set it in one click.` · `Lock to keep this wallpaper. Unlock to let it change.` · `Favorite: keep this one forever`
- Accessibility: `Current wallpaper`, `Share this wallpaper`, `Keep this wallpaper`, `Favorite`, `Change wallpaper now`, `Wallpaper of the day`
- Button: `Change now` · `Try after {N} min` · phase words
- Share sheet: title `A wallpaper for you`, text `A wallpaper I like. Open this link to set it on your computer in one click:`
- Today: `Wallpaper of the day` · `Photo by` `{by}` `on Unsplash` · `Set as wallpaper` · `Finding it…` · `On your desktop ✓`
- Banners: `Couldn't change the wallpaper` / `{error} Your current wallpaper was kept.` · `Hang on` / `Another change is already in progress.` · `Slow down a little` / `You can use Change now twice an hour. Try again in {N} min.` · `Last change didn't work` / `{error} Retrying automatically at {HH:mm}.` · `Couldn't share` / `{message}`

### Search
- Placeholder `Search wallpapers, like northern lights or tokyo night` · `Search` · tooltip `Clear`
- `Need an idea?` · `Shuffle`
- `Interests` · `{on} of {all} on. Each new wallpaper comes from one of them.`
- `Results for “{query}”` · `Add to Interests` (tooltip `Add this search to your Interests, so the daily wallpaper can come from it`)
- `No photos found` · `Try other or fewer words. Only photos big enough for your screen are shown.`
- Result: tooltip `Photographer on Unsplash` · `Set as wallpaper` · `Wallpaper set ✓` · `Show more`
- Banners: `Couldn't search` / message · `Couldn't set the wallpaper` / `{error}` or `Try again.`

### Interests
- `Interests` · `Select all` · `Clear` · tooltip `Back`
- `Nothing is on, so Oriel uses general landscape photos.`
- `Your own` · `Search for anything, then press Add to Interests to make it one of yours.`
- `Finding a photo…` · `Photo comes soon` · tooltip `Remove` · tile tooltip `Photo by {name} on Unsplash`

### History / Favorites
- `History` · `Favorites` · tooltips `Grid`, `List`
- `Nothing here yet.` · `No favorites yet. Star a photo to keep it here.`
- Tooltips: `Set as wallpaper again` · `Favorite: never auto-delete` · **Mac** `Show in Finder` · `Delete` · `Photographer on Unsplash`
- `{EEE, d MMM yyyy} · {topic}`
- Delete dialog: `Delete this wallpaper?` · `The file will be deleted, and Oriel won't pick this photo again.` · `This is a favorite. The file will be deleted, and Oriel won't pick this photo again.` · `Delete` · `Cancel`
- `Couldn't re-apply: {message}`

### Friends
- `Friends` · `Following {Name}` · `Photo by {name}` · `Their wallpaper arrives in a moment.` · `Unfollow`
- `Following your friend…`
- `Share my wallpaper` · `Off. Friends get every wallpaper you set.` · `On.` · `On. No one follows yet.` · `On. 1 person follows you.` · `On. {n} people follow you.` · `Turn off` · `Share` · `Getting your link…` · `Link copied`
- `Follow a friend` · `Use a friend's wallpaper.` · `Enter code`
- `Followers` · `No one yet` · `1 person` · `{n} people` · `See all`
- Code sheet: `Follow a friend` · placeholder `K7MQ-2XPA` · `Follow` · `Cancel`
- Followers sheet: `Followers` · `No one yet. Share your wallpaper and friends who follow show up here.` · `Since {MMM d}` · `Remove` / `Remove?` · `Done`
- Dialogs: `Stop following {Name}?` / `Oriel goes back to your own wallpapers.` / `Unfollow` · `Turn off sharing?` / `Your link stops working.` + ` The 1 person following you stops getting your wallpaper.` | ` The {n} people following you stop getting your wallpaper.` / `Turn off`
- Share sheet: title `Follow my wallpaper`, text `Follow my wallpaper on Oriel: whenever I change mine, yours changes too.`
- Errors: `A code has 8 letters and numbers, like K7MQ-2XPA.` · `No one has that code. Check it and try again.` · `That's your own code. Send it to a friend instead.` · `You seem to be offline. Try again.` · `Couldn't follow with that code. Try again.` · `Couldn't create a code. Try again.` · `Couldn't stop sharing. Try again.` · `Couldn't remove them. Try again.` · `Sign in to Oriel first.` · `{Name} stopped sharing their wallpaper.` / `Your friend stopped sharing their wallpaper.` · default names `A friend`, `your friend`
- Share link errors: `Only photos from Unsplash can be shared.` · `Couldn't make a link. Try again.`

### Settings (Mac version)
- Sections: `Account`, `Schedule`, `Display`, `Quality`, `Storage`, `App`, `Help`
- `Signed in` · `Manage account` · `Sign out`
- `Change wallpaper` / `Once a day, or as a slideshow that keeps changing through the day.`
- `Pick from` / `Favorites cycles through the photos you starred in History. With none, new photos are used.` · `New photos` · `My favorites`
- `Daily change time` / `A new wallpaper once a day at this time.`
- **Mac** `Change at login` / `If today's wallpaper isn't set yet when you log in, change it right away.`
- `Next wallpaper shortcut` · placeholder `Click, then press keys` · `Clear` · `{shortcut} is already used by another app. Pick a different shortcut.`
- `Fit` / **Mac** `How the photo fits your screen. Span stretches one photo across all displays.` · `Fill` `Fit` `Stretch` `Center` `Span`
- `Match the time of day` / `Darker photos in the evening and in dark mode, brighter ones in the morning.`
- **Mac** `A photo for each display` / `Give each display its own photo instead of the same one everywhere.`
- `Skip photos with few likes` / `Prefer photos other people liked on Unsplash.` · `Minimum likes`
- `Wallpaper folder` / `{path}` · `Open` · `Change` · tooltip `Pick an empty folder; your photos move there.` · `Moved 1 photo.` · `Moved {n} photos.` · `Pick an empty folder. Oriel moves your photos into it.` · `A wallpaper change is in progress. Try again in a moment.` · `Couldn't move the photos: {reason} Nothing was changed.`
- **Mac** `Open at login` / `Start in the menu bar when you log in.` · dialog `Turn off Open at login?` / `Oriel won't open when you log in, so the menu bar icon and your shortcut won't work until you open it yourself. Scheduled wallpaper changes will still run.` / `Turn off` / `Keep on`
- `Updates` / `Version {x} beta · Oriel checks for updates once a day and installs them in the background.` · `Version {x} beta · Updates work in the installed app.` · `Check for updates`
- `Contact support` / `Questions, bugs or ideas? Message the Oriel support bot on Telegram.` · `Open Telegram`

### Shared photo card
- `{From} shared a wallpaper with you` / `Someone shared a wallpaper with you` · `It's your wallpaper now`
- `Getting the photo…` · `Photo by **{name}** on Unsplash` · `Not now` · `Set as wallpaper` · `Done`
- `Couldn't open that photo` · `That photo isn't available any more.` · `Close` · `Couldn't set the wallpaper. Try again.`

### Menu bar, popup
- `Next wallpaper` · `Open Oriel` · `Pause daily changes` · `Quit Oriel` · tooltip `Oriel` / `Oriel (paused)`
- Phase words: `Finding a photo…` `Downloading…` `Changing…` `Syncing…` `Receiving…`

### Core errors (shown in banners)
- `Sign in to Oriel first.` · `Another change is already in progress.` · `Something went wrong: {message}`
- `This build of Oriel has no Unsplash access key.` · `Unsplash rejected Oriel's access key.` · `Unsplash refused the request (403).` · `Unsplash hourly limit reached. Will try again later.` · `No photos found for that topic.` · `That photo isn't available (any more).` · `Unsplash returned HTTP {code}.` · `Can't reach Unsplash. Are you offline?` · `Unsplash took too long to answer.` · `Unsplash returned data Oriel couldn't read.`
- `The photo is taking too long to download. Check your connection and try again.` · `Couldn't download the photo. Check your connection and try again.` · `Downloaded file isn't a valid image; keeping your current wallpaper.`
- `Couldn't find a new photo that passes your filters. Try more topics or looser filters.`
- `Searching is paused for a while so your daily wallpaper still gets its photos. Try again in the next hour.`
- **Mac** `macOS refused to set the wallpaper: {message}` · `That image file is gone from disk.` · `Couldn't set it. Try again.`

---

## Appendix B: built-in data

### B.1 Built-in interests (in this order)

```
nature, mountains, ocean, beach, lakes, waterfalls, forest, autumn,
flowers, desert, snow, northern lights, night sky, space, sunset, cities,
city at night, architecture, japan, islands, wildlife, countryside, aerial, fog
```
On by default: **nature, mountains, ocean, space, architecture**. Labels capitalize the first letter
("Northern lights"). Fallback topic with none on: `landscape`.

Emojis per interest exist in the Windows code (`TopicViewModel.Emojis`) but **are not shown anywhere** in the UI;
skip them.

### B.2 Interest cover photos

URL = `https://images.unsplash.com/{path}&w={width}&h={width*3/4}&fit=crop&q=70&fm=jpg` (each path already contains
`?ixid=…`; default width 480). Tooltip/credit: `Photo by {by} on Unsplash`.

| Interest | Path | By |
|---|---|---|
| nature | `photo-1428908728789-d2de25dbd4e2?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk3fA` | Dominik Schröder |
| mountains | `34/BA1yLjNnQCI1yisIZGEi_2013-07-16_1922_IMG_9873.jpg?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk4fA` | Ryan Schroeder |
| ocean | `photo-1443397646383-16272048780e?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk4fA` | Mickey O'neil |
| space | `photo-1419242902214-272b3f66ee7a?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk5fA` | Vincentiu Solomon |
| cities | `photo-1417632993443-302f4897cf67?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk5fA` | Anders Jildén |
| architecture | `photo-1423768164017-3f27c066407f?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NTk5fA` | Anthony DELANOIX |
| forest | `photo-1415650328328-1909c4ceabdb?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NjAxfA` | Kristy Kravchenko |
| desert | `photo-1457264635001-828d0cbd483e?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NjAyfA` | Joshua Sortino |
| aerial | `photo-1454789591675-556c287e39e2?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NjAyfA` | NASA |
| night sky | `photo-1427501482951-3da9b725be23?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NjAzfA` | Michael Hull |
| snow | `reserve/unsplash_52cee6d9df810_1.JPG?ixid=M3wxMDkxNjg3fDB8MXxyYW5kb218fHx8fHx8fHwxNzkxMjg3NjAzfA` | Caleb George |
| beach | `photo-1507525428034-b723cf961d3e?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8Mnx8YmVhY2h8ZW58MXwwfHx8MTc5MTgyNzc2MHww` | Sean Oulashin |
| lakes | `photo-1494500764479-0c8f2919a3d8?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8OHx8bGFrZXN8ZW58MXwwfHx8MTc5MTgyNzc2MXww` | Ken Cheung |
| waterfalls | `photo-1476610182048-b716b8518aae?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8Mnx8d2F0ZXJmYWxsc3xlbnwxfDB8fHwxNzkxODI3NzYyfDA` | Robert Lukeman |
| autumn | `photo-1523712999610-f77fbcfc3843?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MXx8YXV0dW1ufGVufDF8MHx8fDE3OTE4Mjc3NjJ8MA` | Johannes Plenio |
| flowers | `photo-1490750967868-88aa4486c946?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MXx8Zmxvd2Vyc3xlbnwxfDB8fHwxNzkxODI3NzYzfDA` | Sergey Shmidt |
| northern lights | `photo-1475518845976-0fd87b7e4e5d?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MTB8fG5vcnRoZXJuJTIwbGlnaHRzfGVufDF8MHx8fDE3OTE4Mjc3NjR8MA` | v2osk |
| sunset | `photo-1503803548695-c2a7b4a5b875?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8Mnx8c3Vuc2V0fGVufDF8MHx8fDE3OTE4Mjc3NjR8MA` | Sebastien Gabriel |
| city at night | `photo-1444723121867-7a241cacace9?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8NXx8Y2l0eSUyMGF0JTIwbmlnaHR8ZW58MXwwfHx8MTc5MTgyNzc2NXww` | Henning Witzel |
| japan | `photo-1466354424719-343280fe118b?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MTV8fGphcGFufGVufDF8MHx8fDE3OTE4Mjc3NjZ8MA` | Sora Sagano |
| islands | `photo-1502085671122-2d218cd434e6?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8NXx8aXNsYW5kc3xlbnwxfDB8fHwxNzkxODI3NzY2fDA` | Michael |
| wildlife | `photo-1504006833117-8886a355efbf?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MTR8fHdpbGRsaWZlfGVufDF8MHx8fDE3OTE4Mjc3Njd8MA` | Geranimo |
| countryside | `photo-1470240731273-7821a6eeb6bd?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MTF8fGNvdW50cnlzaWRlfGVufDF8MHx8fDE3OTE4Mjc3Njh8MA` | Joel Holland |
| fog | `photo-1504701954957-2010ec3bcec1?ixid=M3wxMDkxNjg3fDB8MXxzZWFyY2h8MTR8fGZvZ3xlbnwxfDB8fHwxNzkxODI3NzY5fDA` | Stefano Pollio |

Onboarding hero photos: mountains, northern lights, ocean (width 600).

### B.3 Search ideas (8 random, no repeats, each time)

```
snowy mountains, alpine lake, dolomites, patagonia, swiss alps, himalayas, mountain sunrise, volcano,
rolling hills, tuscany hills, scottish highlands, mount fuji, rocky mountains, mountain cabin, glacier, canyon,
grand canyon, fjord, norway fjords, iceland landscape,
calm ocean, ocean waves, turquoise water, tropical beach, lighthouse, waterfall, misty lake, river valley,
coral reef, underwater, sailboat, rainy window, rain drops, ice cave, frozen lake, black sand beach,
cliffs by the sea, harbor at dawn, lagoon, surf,
foggy forest, pine forest, autumn leaves, bamboo forest, cherry blossoms, lavender field, sunflower field,
tulips, moss, ferns, redwoods, birch trees, jungle, rainforest, wildflowers, olive trees, palm trees,
botanical garden, succulents, monstera,
northern lights, milky way, starry sky, full moon, galaxy, nebula, earth from space, sunset clouds,
golden hour, blue hour, storm clouds, lightning, rainbow, solar eclipse, night sky desert, cotton candy sky,
sunrise, pink sky, comet, planets,
tokyo night, city skyline, new york, paris streets, london rain, hong kong, dubai skyline, seoul at night,
venice canals, amsterdam, istanbul, rooftops, neon signs, city lights from above, empty street,
subway station, bridge at night, old town, cafe street, tram,
brutalism, modern architecture, spiral staircase, minimal building, glass building, cathedral, temple,
mosque, japanese temple, old library, arches, concrete, interior design, scandinavian interior,
geometric architecture, skyscraper, lighthouse at night, windmill, castle, ruins,
desert dunes, sahara, joshua tree, salt flats, red rocks, monument valley, oasis, cactus, badlands,
desert road, wadi rum, namib desert, sandstone, dry lake, atacama,
snowy forest, winter village, frost, snowfall, ice, polar bear, snowy cabin, winter road, ski slope,
arctic, antarctica, frozen waterfall, winter sunrise, snowy pines, christmas lights,
fox, deer in forest, wolf, owl, whale, jellyfish, horses, cat, dog, birds flying, eagle, lion,
elephant, butterfly, koi fish,
minimal landscape, black and white, dark moody, pastel, gradient, abstract shapes, texture, marble,
paper texture, smoke, ink in water, bokeh, long exposure, light trails, neon, geometry, shadows,
silhouette, fog, monochrome,
aerial beach, aerial forest, drone city, road trip, winding road, aerial river, farmland from above,
rice terraces, salt pans, highway at night,
spring morning, summer evening, autumn forest, cozy, calm, dreamy, vintage, film photography, sunlight through trees,
golden fields, lonely tree, countryside, village, picnic, hot air balloons, cappadocia, santorini, bali,
kyoto, morocco
```

### B.4 Links

| Name | URL |
|---|---|
| Login | `https://oriel.akbarjon.uz/login` (`?method=email`) |
| Account page | `https://oriel.akbarjon.uz/me` |
| Share page | `https://oriel.akbarjon.uz/share/{id}` |
| Follow page | `https://oriel.akbarjon.uz/follow/{code}` |
| Wallpaper of the day list | `https://oriel.akbarjon.uz/today.json` |
| Support (Telegram bot) | `https://t.me/oriel_support_bot` |
| Releases (Mac) | `https://github.com/oriel-releases/macos` |

---

## Appendix C: icon map

Windows Segoe Fluent glyph → SF Symbol to use.

| Windows | Meaning | SF Symbol |
|---|---|---|
| E80F | Home | `house` |
| E721 | Search | `magnifyingglass` |
| E81C | History | `clock.arrow.circlepath` |
| E734 / E735 | Star / filled star | `star` / `star.fill` |
| E716 | People | `person.2` |
| (settings) | Settings | `gearshape` |
| E72D | Share | `square.and.arrow.up` |
| E73E | Check | `checkmark` |
| E785 / E72E | Unlock / Lock | `lock.open` / `lock.fill` |
| E72C | Refresh (Change now) | `arrow.clockwise` |
| E121 | Clock (limited) | `clock` |
| E8B3 | Change wallpaper row | `photo.on.rectangle` |
| E8B9 | Pick from row | `photo.stack` |
| E823 | Time | `clock` |
| E77B | Sign-in / login row | `person.crop.circle` |
| E765 | Keyboard | `keyboard` |
| E740 | Fit | `rectangle.expand.vertical` |
| E708 | Moon (time of day, "quiet and light") | `moon` / `moon.stars` |
| E7F4 | Monitor | `display` / `display.2` |
| E8E1 | Likes | `hand.thumbsup` |
| E76E | Minimum likes | `slider.horizontal.3` |
| E838 | Folder | `folder` |
| E7E8 | Power (start at login) | `power` |
| E895 | Updates | `arrow.triangle.2.circlepath` |
| E8F2 | Chat (support) | `bubble.left` |
| EB51 | Heart (interests) | `heart` |
| E76C | Chevron right | `chevron.right` |
| E72B / E72A | Back / forward | `chevron.left` / `chevron.right` |
| E8B1 | Shuffle | `shuffle` |
| E710 | Add | `plus` |
| E711 | Close / clear / remove | `xmark` |
| E7A7 | Set again | `arrow.uturn.backward` |
| ED25 | Open in folder | `folder` (Show in Finder) |
| E74D | Delete | `trash` |
| F0E2 / E8FD | Grid / list view | `square.grid.2x2` / `list.bullet` |
| E8FA | Add friend | `person.badge.plus` |
| E787 | Calendar | `calendar` |

---

## Appendix D: Windows → macOS mapping table

| Windows piece | File | macOS |
|---|---|---|
| WinUI 3 + Mica | `MainWindow.xaml` | SwiftUI window, native sidebar material, `paper` content |
| NavigationView | `MainWindow.xaml` | `NavigationSplitView` sidebar |
| InfoBar | everywhere | custom `InfoBar` banner view |
| ContentDialog | Friends, History, Settings | `.alert` (confirmations), `.sheet` (code entry, followers) |
| ToggleSwitch / ComboBox / Slider / TimePicker | Settings | `Toggle(.switch)` / `Picker(.menu)` / `Slider` / custom 24 h 5-min time field |
| ItemsRepeater UniformGridLayout | Search, History, Interests | `LazyVGrid(columns: [.adaptive(minimum: N)])` |
| Tray icon (H.NotifyIcon) | `TrayIconService.cs` | `NSStatusItem` + `NSMenu` |
| Toast notifications | `Toasts.cs` | **Not needed**: Windows registers toasts but never sends one |
| Global hotkey (RegisterHotKey) | `HotkeyService.cs` | Carbon `RegisterEventHotKey` |
| Change popup (borderless Win32 window + DWM acrylic) | `ChangePopup.cs` | Non-activating `NSPanel` + `NSVisualEffectView` |
| Share sheet (DataTransferManager) | `ShareSheet.cs` | `NSSharingServicePicker` |
| Clipboard | pages | `NSPasteboard.general` |
| Explorer "/select" | `ImageLoader.cs` | `NSWorkspace.activateFileViewerSelecting` |
| FolderPicker | `SettingsPage.xaml.cs` | `NSOpenPanel` |
| Launcher.LaunchUriAsync | many | `NSWorkspace.shared.open(url)` |
| SystemParametersInfo / IDesktopWallpaper | `WindowsWallpaperSetter.cs` | `NSWorkspace.setDesktopImageURL` per `NSScreen` |
| Virtual desktops (all) | `VirtualDesktops.cs` | Re-apply on Space change |
| Lock screen | `LockScreenSetter.cs` | Not needed (macOS shows the wallpaper) |
| Accent color setter | `AccentSetter.cs` | Not possible; only sampling for sync |
| Accent sampler (WinRT decoder) | `AccentSampler.cs` | `CGImageSource` 96×54 + `AccentPicker` |
| Dark mode (registry) | `SystemTheme.cs` | `NSApp.effectiveAppearance` / `AppleInterfaceStyle` |
| DPAPI account file | `AccountStore.cs` | `auth.json` 0600 |
| Run key (Start with Windows) | `SystemIntegration.cs` | `SMAppService.mainApp` |
| Task Scheduler (ApplyToday, Retry) | `SystemIntegration.cs` | launchd agents `uz.akbarjon.oriel.apply` / `.retry` |
| Protocol registration (registry) | `SystemIntegration.cs` | `CFBundleURLTypes` in Info.plist |
| Single instance (AppInstance) | `Program.cs` | LaunchServices + `NSRunningApplication` check |
| Efficiency mode, MemoryTrim | `EfficiencyMode.cs`, `MemoryTrim.cs` | App Nap, `.accessory` policy, release images |
| FastConnect (happy eyeballs) | `FastConnect.cs` | Not needed (URLSession does it) |
| Velopack updates | `UpdateService.cs` | Sparkle 2 |
| Cross-process lock (FileShare.None) | `WallpaperService.cs` `ApplyLock` | `flock` on `apply.lock` |
| CommunityToolkit.Mvvm view-models | `ViewModels/` | `ObservableObject` classes with `@Published` |
| Microsoft.Extensions.DependencyInjection | `ServiceCollectionExtensions.cs`, `AppHost.cs` | One `AppHost` composition root creating singletons |

---

*End of the task. Build it phase by phase (§47), check each phase, and keep it identical to Windows wherever macOS
allows. When something here is unclear and `windows/` is available, read the Windows file named in that section.*
