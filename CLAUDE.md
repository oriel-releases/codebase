# Oriel

A free wallpaper app: a fresh Unsplash photo on a schedule, the same on every device of an account.

| Part | Folder | Stack | Released to |
|---|---|---|---|
| Windows app | `windows/` | C#, WinUI 3, Velopack | `oriel-releases/windows` |
| Linux app | `linux/` | C#, Avalonia, Velopack AppImage | `oriel-releases/linux` |
| Android app | `android/` | Kotlin, Jetpack Compose | `oriel-releases/android` |
| macOS app | `macos/` | Swift 6, SwiftUI + AppKit, Sparkle | `oriel-releases/macos` |
| Browser extension | `extension/` | TypeScript, Vite (new tab page) | Chrome / Edge / Firefox stores |
| Website | `website/` | Next.js, static export, Firebase Hosting | https://oriel.akbarjon.uz |

Backend: Firebase project **`oriel-gcp`** only (Auth, Firestore, Realtime Database, Hosting). Hard constraint:
everything must stay **free** (Spark plan, no Blaze, no card, no Cloud Functions) and light on phone battery.

## Repositories

- **Private, source code:** https://github.com/akbarjorayev/oriel (`origin`; the whole repo root, branches like `release-1.0.8`, default `main`).
- **Public, releases only** (org `oriel-releases`, so the updaters read them without a token):
  - https://github.com/oriel-releases/windows. Moved from `akbarjorayev/oriel-public`, a transferred repo: **never create a
    new repo with that old name**, or installed apps (1.0.4 and older) lose their redirect and stop updating.
  - https://github.com/oriel-releases/linux
  - https://github.com/oriel-releases/android (`Oriel.apk` on each release; the app installs it from `releases/latest`)
  - https://github.com/oriel-releases/macos (`Oriel.dmg`, `Oriel.zip` and `appcast.xml`; Sparkle reads
    `releases/latest/download/appcast.xml`)

## Workflow rules

- **Pushing code:** never push straight to `main`. Create a branch (e.g. `release-1.0.8`, or a short feature name),
  split the work into meaningful commits, and open a **pull request** into `main`.
- **Releasing a new version** of an app:
  1. Bump the version: `<Version>` in `windows/src/Oriel.App/Oriel.App.csproj` / `linux/src/Oriel.App/Oriel.App.csproj`,
     or `oriel.version` in `android/gradle.properties` (`android/scripts/release.ps1 -Version x.y.z` sets it; versionCode follows).
  2. Run its script with `GITHUB_TOKEN` set (Contents: write on the `oriel-releases` repo):
     `windows/scripts/release.ps1`, `linux/scripts/release.sh` (on Linux or **WSL**: `wsl -d Ubuntu`, it has .NET and vpk),
     `android/scripts/release.ps1`. Each has `-NoUpload` / `--no-upload` to only build locally.
  3. Update the Firestore version document, field `version`: **`application/windows`**, **`application/linux`** or
     **`application/android`** (the old `application/version` is unused). The website reads them at build time and again
     in the browser on the download page; the rules allow reading exactly these three.
  4. Update that app's entry in `fallbackVersions` and its `releasedOn` in `website/src/config/release.ts`.
  5. Rebuild and redeploy the website (`npm run build`, then `firebase deploy --only hosting`). If rules changed, also
     `firebase deploy --only firestore:rules` / `--only database`.
- **Releasing the macOS app:** bump `MARKETING_VERSION` and `CURRENT_PROJECT_VERSION` in `macos/project.yml`, merge,
  then push a tag `macos-vX.Y.Z`: `.github/workflows/macos-release.yml` tests, builds, signs ad hoc and publishes to
  `oriel-releases/macos` as `vX.Y.Z` (or run `macos/scripts/release.sh` on a Mac with `GITHUB_TOKEN` set). Then the
  Firestore document **`application/macos`** and the website steps 4 and 5 above.
- **Releasing the Linux app from GitHub:** instead of step 2 above, merge the version bump and push a tag `linux-vX.Y.Z`:
  `.github/workflows/linux-release.yml` tests, builds and publishes to `oriel-releases/linux` (secrets
  `LINUX_UNSPLASH_ACCESS_KEY` and `LINUX_RELEASES_TOKEN`, or `RELEASES_TOKEN` if it covers that repo). Steps 3 to 5 stay.
- **Releasing the extension:** bump `version` in `extension/package.json`, `npm run package`; folders for Chrome, Edge,
  Firefox and the source land in `extension/release/` (earlier versions move to `release/old/<version>/`). The owner
  zips and uploads them to the stores.

## Android app (built, 1.0.0 released)

- Native **Kotlin + Jetpack Compose**, Android 8.0+ (see `android/README.md`). **No Firebase SDK**: Auth, Firestore and
  the Realtime Database are REST calls (`data/Firebase.kt`), like the desktop `FirebaseClient.cs`.
- Five tabs: **Home** (the wallpaper in a phone-shaped frame on its blurred self, Share, Lock, Crop, Favorite, Change
  now), **Search** (Interests live inside it, added only via "Add to Interests"), **AI** (AI Studio, see Create below),
  **History** (All / Favorites), **Settings** (Friends lives inside it, a row that opens it). Floating blurred navigation bar (Haze), a rail on the right in landscape.
  Black and white design, Lucide-style icons, every dialog a bottom sheet rounded on top only.
- Sign-in: **Google via Credential Manager** (native, no browser) or email and password.
- Scheduling: one inexact alarm at the next tick (no exact-alarm permission), then a JobScheduler job does the change.
  A periodic job (~15 min, network + battery not low) checks the account's photo, settings and favorites.
- **Photo position** (this phone only, not synced): Smart (`crop=faces,entropy`, the default), Center, or a custom focal
  point per photo from the editor (`crop=focalpoint&fp-x&fp-y`). Stored in SharedPreferences `positions`.
- Updates itself from `oriel-releases/android` on launch and after each wallpaper change (`core/Updater.kt`).
- **Builds:** `release` = `uz.akbarjon.oriel`, minified, signed with the **release key**; `debug` and `staging` =
  `uz.akbarjon.oriel.dev` with the debug key (`staging` is non-debuggable, so it runs as fast as release: use it to try
  things on a phone, `./gradlew assembleStaging` + `adb install -r`). `JAVA_HOME` = Android Studio's `jbr`.
- **Release key:** `android/oriel-release.jks` + `android/keystore.properties` (both git-ignored, backed up by the owner).
  Every release must be signed with it: Android refuses an update signed differently, so losing it means everyone reinstalls.
- **Two Firebase Android apps**, only for Google sign-in (Google checks package name + signing key fingerprint):
  `uz.akbarjon.oriel.dev` with the debug key's SHA-1, and `uz.akbarjon.oriel` with the release key's SHA-1/SHA-256.
  Both use the same web client ID for Credential Manager. A new signing key means adding its fingerprint there.

## macOS app (built, 1.0.0 released)

- Native **Swift 6, SwiftUI + AppKit**, macOS 26 Tahoe+, a menu bar app (see `macos/README.md`; the full specification
  is `macos/docs/mac-task.md`). Core logic in the `OrielKit` Swift package, ported from `windows/src/Oriel.Core`
  (same sync protocol, schedule math and slot IDs; `source: "macos"`, session `platform: "macos"`, stats `macos_*`).
- Sign-in through the website (`oriel://auth` links), account in `~/Library/Application Support/Oriel/auth.json` (0600,
  not the Keychain: ad-hoc signatures change every update). Updates with **Sparkle** (EdDSA key: back it up; losing it
  means installed copies never update again). Not notarized: first launch needs Privacy & Security > Open Anyway.
- Background fallback when the app is closed: a launchd agent `uz.akbarjon.oriel.apply` runs `Oriel --apply-today`.
- **Builds:** Release = `uz.akbarjon.oriel`, Debug = `uz.akbarjon.oriel.dev`. Project generated by XcodeGen
  (`macos/scripts/configure.sh`), Unsplash key and Sparkle public key from `macos/.env`.

## Browser extension (built)

- Code in `extension/` (see its README): new tab page for Chrome/Edge/Brave/Firefox. "Same as my devices" shows the
  account's `sync/current`; "Its own" picks from a bundled pool (`extension/src/data/pool.json`).
- Its settings and **shortcuts** (max 14, drag to reorder) are its own, not the apps': kept in the browser, and when
  signed in synced to `users/{uid}/sync/extension` = {settings, shortcuts[{title, url}], updatedAt, device}, last write
  wins, rung on the doorbell as {extensionAt, extensionDevice}. Local first: a tab reads `sync/extension` only when the
  doorbell's `extensionAt` is newer than its `ext-version`, and `sync/current` only when the doorbell names another
  photo. Signing out erases the shortcuts from that browser.
- Settings panel (Home, Interests, Settings, Help, Account) on the new tab and as the toolbar **popup** (`popup.html`);
  on an Oriel new tab the icon opens the page's own panel instead (`src/background.ts`). Wallpaper of the day comes
  from `src/data/today.json`, written together with the website's by `website/scripts/build-today.mjs`.
- Sign-in, in Settings > Account: **email and password right in the panel** (`signInWithEmailAndPassword`, with
  password reset), or **Google** through the website: `/login?for=extension` posts the Google credential to the
  extension's content script (`src/content/bridge.ts`). Either way it records a `users/{uid}/sessions/{id}`.
- Firebase is loaded lazily and uses `firestore/lite`; live updates come from the RTDB doorbell over a plain
  `EventSource`, then `sync/current` is read once per ring. In "Same as my devices" its shuffle publishes to every device.

## Cross-device sync (built; Firestore rules in `website/firestore.rules`, RTDB rules in `website/database.rules.json`)

- **Current photo:** every change anywhere (scheduled, Change now, Search, History, a share) is published to
  `users/{uid}/sync/current` (the source of truth) = {photoId, imageUrl (raw Unsplash URL; each device crops its own),
  pageUrl, photographer, photographerUrl, description, topic, color (#RRGGBB accent), source
  (windows|linux|android|extension), updatedAt}. Sync the **Unsplash photo ID**, never the image file.
- **Doorbell:** after publishing, the device rings `pulse/{uid}` in the **Realtime Database** (`oriel-gcp-default-rtdb`)
  with {photoId, source, device (session id), at}. Rings are PATCHes, so photo, settings and favorites fields never
  clobber each other. Desktops (`PulseListener`), Android while on screen (`Pulse`) and the extension keep one SSE
  connection and react within seconds; the first "put" event triggers a full check, later "patch" events dispatch by
  field; `auth_revoked` reconnects with a fresh token. Fallbacks: desktop polls every 3 min (10 while the SSE is up),
  Android checks on open, before each scheduled change and every ~15 min. Spark allows 100 simultaneous RTDB
  connections; beyond that devices fall back to polling.
- **No echo:** a photo from another device of the account isn't republished; a followed friend's photo is (so the
  account's other devices get it too).
- **Shared settings** `users/{uid}/sync/settings` = {dailyTime, timeZone (IANA), intervalMinutes, paused,
  favoritesOnly, skipLowLikes, minLikes, matchTimeOfDay, interests[{query, enabled, isCustom}], updatedAt, device};
  last write wins; a change rings {settingsAt, settingsDevice}. First sign-in adopts the account's settings, or uploads
  its own if there are none. **Device-only, never synced:** lock screen / home+lock, fit, hotkey, startup, folder,
  change-at-login, Android photo position, search history.
- **Accent color:** Home shows the photo's top 3 colors (`AccentPicker.PickTop`, one per part of the color wheel); the
  chosen one is kept in `OrielState.AccentPhotoId/AccentColor`, becomes the Windows accent (turning "Match accent
  color" on) and is written to `sync/current.color` (masked PATCH) with a doorbell ring. Receiving desktops take it in
  `FollowService.CheckAsync`, also for a photo they already show.
- **Lock** (`paused`) stops only the schedule: a photo set by hand on any device still shows everywhere, and setting
  one never unlocks.
- **Favorites** `users/{uid}/favorites/{photoId}` = {photoId, imageUrl, pageUrl, photographer, photographerUrl,
  description, favorite (bool; never deleted, so un-starring syncs), updatedAt, device}; a change rings {favoritesAt,
  favoritesDevice}; devices fetch only docs newer than their last sync (runQuery). A favorite of a photo a device never
  showed is added to its History at its own time (desktop: no file yet, "Set again" downloads it). First sync uploads
  the device's own favorites. Quick repeated taps / clicks on Home's preview favorite it with a star burst (all apps).
- **One clock, first device wins:** ticks = the daily time, then every interval, in the shared IANA zone (desktop
  `ScheduleRules`, Android `Schedule`, identical math; DST via `TimeZoneInfo` / `java.time`). Before picking, a device
  claims the tick with a compare-and-set on `users/{uid}/sync/slot` = {slotId "2026-10-14T09:00", device, at}
  (`currentDocument.updateTime` / `exists=false`). The loser marks the tick done and shows the winner's photo. A schedule
  edit (local or synced) sets `scheduleStartedAt`, so the moved moment isn't treated as missed.
- **Progress words** on every platform while a photo changes: Finding a photo… / Downloading… / Changing… /
  Syncing… / Receiving… (desktop `ApplyPhase` + `PhaseText` + `PhaseWatch`, Android `Phase`, extension `setStatus`),
  sliding from one to the next.

## Other Firestore features

- **Follow a friend:** `codes/{code}` = {uid, name, picture, createdAt, photoId, photoPath, photographer,
  photographerHandle} (8 chars, no 0/O/1/I; get only, signed in, never listed; the apps keep the photo fields on the
  owner's current wallpaper). Link `/follow/{code}` (signed-in visitors; opens `oriel://follow?code=`). Following writes
  `users/{friendUid}/followers/{myUid}` = {code, name, createdAt}, which lets the follower read the friend's
  `sync/current` and nothing else. While following, the own schedule is off.
- **Share links:** `/share/{id}` → `shares/{id}` = {uid, from (first name), picture, photoId, photoPath, photographer,
  photographerHandle, createdAt}, readable only when signed in. Old `/share?id=&i=&by=&u=&from=` links still work. A share
  or follow opened without the app rides along the sign-in link (`oriel://auth?...&share=ID&from=Name&follow=CODE`).
  Hosting rewrites `/share/**` and `/follow/**` to their pages (production only; `next dev` doesn't).
- **Anonymous daily counts:** `stats/{YYYY-MM-DD}` (UTC), numbers only, written by the desktop apps (`StatsReporter`):
  `windows_new`, `linux_new`, `windows_active`, `linux_active`, `retained7`, `retained30`, `sharesSent`, `sharesOpened`,
  `sharesSet`. Rules allow only +1 on today's doc. Keep it this small: no per-click events, no user IDs.
  Last online = `sessions/{id}.lastSeenAt`.
- **Wallpaper of the day:** `/today`, photos from `website/src/data/today.json` (`npm run today` rebuilds it from the
  extension's pool).

## Notes

- Desktop apps talk to Firebase over **REST** (`Oriel.Core/Auth/FirebaseClient.cs`); there is no .NET desktop SDK.
  `windows/` and `linux/` share the same `Oriel.Core` design: change one, change the other.
- Keep Firestore traffic to single small documents (no listing whole collections) to stay inside Spark quotas.
- The Unsplash key is shared and rate-limited: first-device-wins means one pick per tick, and Change now is limited to
  twice an hour on Android. Respect Unsplash terms (hotlink URLs, ping the download tracking endpoint, credit the photographer).
- Wallpapers are fetched at full quality (`q=100`, at most 2160px wide).
- **AI (AI wallpapers):** the "AI" page on desktop (left menu) and Android (tab, "AI Studio"); "I'm feeling lucky" makes
  one from a random idea and look. Android always makes 16:9 (2048 x 1152, so it fills the computers it syncs to) and
  crops its own part on the phone (`Wallpapers.fetch`; the position editor works for it). The user's few words are written out to a 60-90 word scene by
  `openai/gpt-5.4-nano`, then drawn by `zimage` at the screen's shape (at most 2048 wide; 2560 is refused), both on
  Pollinations with the owner's key `POLLINATIONS_KEY` baked in like the Unsplash key (it ships in the app: keep a Pollen
  budget on it). 20 pictures per device per day (`created.json`). Pollinations keeps each picture at a public link
  `https://media.pollinations.ai/{id}` (30 days after last use), so it syncs like a photo: `photoId` = `ai-{id}`,
  `imageUrl` = that link (rules `validImage`), favorites too. Other devices download the link (desktop `ApplyLinkedAsync`;
  Android and the extension use it as the image URL as is). Not shareable by link or follow code yet.
- **History sync** across devices, including deletes (tombstones). Today only favorites and the current photo sync.
- **Instant delivery to a closed Android app:** FCM data messages sent by a free Cloudflare Worker (it verifies the
  Firebase ID token, reads device FCM tokens, sends the push; key only as a Worker secret). Today a closed app catches
  up on its ~15 min job, before each scheduled change, or when opened.
- A **time zone picker** on desktop (it has `TimeZone`, but it's only editable on the phone).
- Syncing the Android photo position (focal point) with the photo, for a future iPhone app.
- The desktop onboarding tour on Android.
