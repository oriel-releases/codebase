# Oriel

A free wallpaper app: a fresh Unsplash photo on a schedule, the same on every device of an account.

| Part | Folder | Stack | Released to |
|---|---|---|---|
| Windows app | `windows/` | C#, WinUI 3, Velopack | `oriel-releases/windows` |
| Linux app | `linux/` | C#, Avalonia, Velopack AppImage | `oriel-releases/linux` |
| Android app | `android/` | Kotlin, Jetpack Compose | `oriel-releases/android` |
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
- **Releasing the extension:** bump `version` in `extension/package.json`, `npm run package`; folders for Chrome, Edge,
  Firefox and the source land in `extension/release/` (earlier versions move to `release/old/<version>/`). The owner
  zips and uploads them to the stores.

## Android app (built, 1.0.0 released)

- Native **Kotlin + Jetpack Compose**, Android 8.0+ (see `android/README.md`). **No Firebase SDK**: Auth, Firestore and
  the Realtime Database are REST calls (`data/Firebase.kt`), like the desktop `FirebaseClient.cs`.
- Sections like the desktop apps: **Home** (the wallpaper in a phone-shaped frame on its blurred self, Share, Lock, Crop,
  Favorite, Change now), **Search** (Interests live inside it, added only via "Add to Interests"), **History** (All /
  Favorites), **Friends**, **Settings**. Floating blurred navigation bar (Haze), a rail on the right in landscape.
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

## Browser extension (built)

- Code in `extension/` (see its README): new tab page for Chrome/Edge/Brave/Firefox. "Same as my devices" shows the
  account's `sync/current`; "Its own" picks from a bundled pool (`extension/src/data/pool.json`). Its own settings live
  in the extension.
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

## Not built yet (ideas, in rough order)

- **History sync** across devices, including deletes (tombstones). Today only favorites and the current photo sync.
- **Instant delivery to a closed Android app:** FCM data messages sent by a free Cloudflare Worker (it verifies the
  Firebase ID token, reads device FCM tokens, sends the push; key only as a Worker secret). Today a closed app catches
  up on its ~15 min job, before each scheduled change, or when opened.
- A **time zone picker** on desktop (it has `TimeZone`, but it's only editable on the phone).
- Syncing the Android photo position (focal point) with the photo, for a future iPhone app.
- The desktop onboarding tour on Android.
