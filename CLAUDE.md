# Oriel: Android + cross-device sync plan

## Repositories

- **Private, source code:** https://github.com/akbarjorayev/oriel (`origin`; the whole repo root is pushed here, branches like `release-1.0.3`, default `main`).
- **Public, releases only:** https://github.com/oriel-releases/windows (org `oriel-releases`; installers / Velopack releases are published here so the updater can read them without a token). Moved from `akbarjorayev/oriel-public`, which is a transferred repo: **never create a new repo with that old name**, or installed apps (1.0.4 and older) lose their redirect and stop updating.
- **Public, Linux releases only:** https://github.com/oriel-releases/linux (Velopack AppImage releases for the app in `linux/`, built by `linux/scripts/release.sh`).

## Workflow rules

- **Pushing code to the private repo:** never push straight to `main`. Create a branch (e.g. `release-1.0.4`, or a short
  feature name) and open a **pull request** into `main`.
- **Deploying a new version:** besides `windows/scripts/release.ps1` (publishes to `oriel-releases/windows`) or
  `linux/scripts/release.sh` (publishes to `oriel-releases/linux`), also
  1. update the Firestore version document of that app, field `version` (project `oriel-gcp`): **`application/windows`**
     or **`application/linux`** (the old `application/version` is no longer used); the website reads them at build time
     and again in the browser on the download page,
  2. update that app's entry in `fallbackVersions` and its `releasedOn` in `website/src/config/release.ts`,
  3. redeploy the website (`firebase deploy --only hosting`, after `npm run build`). If `website/firestore.rules`
     changed, also `firebase deploy --only firestore:rules` (it must allow reading both `application/*` documents).

## Browser extension (built)

- Code in `extension/` (see its README): new tab page for Chrome/Edge/Brave/Firefox. Optional link to the Oriel account:
  "Match my desktop" follows `users/{uid}/sync/current`, "Its own" picks from a bundled pool (`extension/src/data/pool.json`).
- Windows and Linux apps publish `sync/current` after every applied wallpaper (`SyncPublisher`, best effort, never fails a change)
  and once at start if it doesn't exist yet. Fields: photoId, imageUrl (Unsplash thumb URL; each device re-crops), pageUrl,
  photographer, photographerUrl, description, topic, source (windows|linux), updatedAt. The extension's own settings live in the extension.
- Sign-in reuses the website: `/login?for=extension` posts the Google credential to the extension's content script.
- When the sync work below is built, keep `sync/current` as the "current photo" document (the plan's `users/{uid}/current` is not a valid
  document path; it lives at `users/{uid}/sync/current`).

## Built since (Firestore, all in `website/firestore.rules`; deploy rules after changing them)

- **Follow a friend:** `codes/{code}` = {uid, name, picture, createdAt, photoId, photoPath, photographer, photographerHandle}
  (8 chars, no 0/O/1/I; get only and only when signed in, never listed; the apps keep the photo fields on the owner's current
  wallpaper). Link: `/follow/{code}` (signed-in visitors only; opens `oriel://follow?code=`). Following writes
  `users/{friendUid}/followers/{myUid}` = {code, name, createdAt}, which lets the follower read the friend's `sync/current`
  (and nothing else). While following, the app's schedule is off and `FollowService` polls that one doc every ~10 min.
- **Anonymous daily counts:** `stats/{YYYY-MM-DD}` (UTC), numbers only, written by the Windows/Linux apps (`StatsReporter`):
  `windows_new`, `linux_new`, `windows_active`, `linux_active`, `retained7`, `retained30`, `sharesSent`, `sharesOpened`,
  `sharesSet`. Rules allow only +1 on today's doc. Read them in the Firebase console. Last online = `sessions/{id}.lastSeenAt`.
  Keep it this small: no per-click events, no user IDs.
- **Share links:** `/share/{id}` → `shares/{id}` = {uid, from (first name), picture, photoId, photoPath, photographer,
  photographerHandle, createdAt}, readable only when signed in (the page asks visitors to sign in first). Old `/share?id=&i=&by=&u=&from=`
  links (apps 1.0.7 and older) still work. A share or follow opened without the app is kept in localStorage and rides along the
  sign-in link (`oriel://auth?...&share=ID&from=Name&follow=CODE`), so it becomes the new user's first wallpaper / friend.
  Hosting rewrites `/share/**` and `/follow/**` to their pages (production only; `next dev` doesn't).
- **Same wallpaper on every device** (Windows, Linux, Android, extension): a change anywhere is published to
  `users/{uid}/sync/current` (Firestore, the source of truth; `source` windows|linux|android|extension), then the
  device rings a doorbell, `pulse/{uid}` = {photoId, source, device (session id), at} in the **Realtime Database**
  (`oriel-gcp-default-rtdb`, rules in `website/database.rules.json`, deploy with `firebase deploy --only database`).
  Desktops (`PulseListener`) and Android while on screen (`Pulse`) keep one SSE connection to it and apply the new
  photo within seconds (`FollowService.CheckAsync` / `Following.check`); the extension listens to `sync/current` with
  `onSnapshot`. Fallbacks: desktop polls every 3 min (10 while the SSE is up), Android checks on open, before each
  scheduled change, and every 15 min in the background. Spark limit: 100 simultaneous RTDB connections; beyond that
  devices fall back to polling. A photo from another device isn't republished (no echo); a followed friend's is.
  In "Same as my devices" mode the extension's shuffle publishes a pool photo to every device.
- **Shared settings** `users/{uid}/sync/settings` = {dailyTime, timeZone (IANA), intervalMinutes, paused, favoritesOnly,
  skipLowLikes, minLikes, interests[{query, enabled, isCustom}], updatedAt, device}; last write wins. A change rings
  `pulse/{uid}` with {settingsAt, settingsDevice} (rings are PATCHes, so photo and settings fields don't clobber each
  other). First sign-in adopts the account's settings, or uploads its own if none (desktop `SettingsSync`, Android
  `SettingsSync`). Device-only settings (lock screen, fit, hotkey, startup, folder) never sync. Desktop now has
  `TimeZone` too (no picker yet; edit on the phone). `paused` (Lock) stops only the schedule: a photo set by hand on
  any device still syncs to every other device, and setting one never unlocks.
- **Favorites, the same everywhere:** `users/{uid}/favorites/{photoId}` = {photoId, imageUrl (raw Unsplash URL),
  pageUrl, photographer, photographerUrl, description, favorite (bool; never deleted, so un-starring syncs), updatedAt,
  device}. A change rings `pulse/{uid}` {favoritesAt, favoritesDevice}; devices fetch only docs with updatedAt newer
  than their last sync (runQuery). A favorite of a photo a device never showed is added to its History, filed at its
  own time (desktop: no file; "Set again" downloads it). First sync uploads the device's own favorites
  (`FavoritesSync` on both). Double-tap / quick repeated taps on Home's preview favorite it with a star burst (all apps).
- **One clock + first device wins:** ticks = daily time, then every interval, in the shared zone (desktop
  `ScheduleRules`, Android `Schedule`, identical math). Before picking, a device claims the tick with a
  compare-and-set on `users/{uid}/sync/slot` = {slotId "2026-10-14T09:00", device, at}; the loser marks the tick done
  and shows the winner's photo from `sync/current`. A schedule edit (local or synced) sets `scheduleStartedAt` so the
  moved moment isn't "missed".
- **Change progress text** on every platform: Finding a photo… / Downloading… / Changing… / Syncing… / Receiving…
  (desktop `ApplyPhase` + `PhaseText`, Android `Phase`, extension `setStatus`), with the words sliding on change.
- **Wallpaper of the day:** `/today`, photos from `website/src/data/today.json` (`npm run today` rebuilds it from the
  extension's pool).
- `sync/current.color` is the photo's accent color (#RRGGBB), set by the apps; the extension tints its UI with it.

Status of the Android + sync plan below: **decided, not built yet** (except the `sync/current` document above). Windows app is in `windows/`, Linux in `linux/`, website in `website/`.
Hard constraint: everything must stay **free** (Firebase Spark plan, no Blaze, no card) and light on phone battery.

## Goal

Same wallpaper on Windows and Android, synced through Firebase. Android app is a **full app like Windows**
(Home, Search, Interests, History, Collections, Settings), not wallpaper-only.

## Android app

- Native **Kotlin + Jetpack Compose**. Not React Native (all the work is OS integration; no code to share, Windows core is C#), not Java.
- `WallpaperManager` for home / lock / both, Firebase Android SDK, Credential Manager for Google sign-in.

## 16:9 vs 9:16

- Sync the **Unsplash photo ID**, never the image file. Each device builds its own crop from the raw URL
  (imgix params, e.g. `?w=1080&h=2340&fit=crop&crop=entropy`, or `crop=faces`).
- A landscape photo cropped to 9:16 keeps ~1/3 of its width. Default plan: smart-crop, and lean towards
  `orientation=squarish` photos so neither screen loses much. Later option: user-adjustable focal point, synced with the photo.

## What syncs (Firestore, `users/{uid}/...`)

Synced:
- **Interests** (the renamed "Daily Mix": topic chips, built-in + custom, `{query, enabled, isCustom}`). Rename in UI and sync data only; C# property `Topics` can stay.
- **History** (metadata + Unsplash ID only; each device re-downloads/crops). **Deletes sync everywhere** (tombstone, `IsDeleted`). Favorites sync too.
- **Current photo.**
- Settings: **daily time, timezone, interval (daily / every N hours), favorites-only, min likes, paused.**

NOT synced:
- Search history.
- Device-specific: lock screen mode (Windows on/off, Android home/lock/both), fit, hotkey, start with Windows, wallpaper folder, apply-at-login.

First sign-in on an existing install **uploads the PC's history, settings, interests and current photo**.

## Scheduling: "first device wins" (no backend)

- Slideshow (hourly etc.) **stays**; changing the wallpaper on a schedule is the core of the app. Daily is just a 24h interval.
- Schedule is anchored at the daily time **in the stored IANA timezone** (e.g. `Asia/Tashkent`), computed identically on every device. First device sets it from its own zone; picker in Settings. Handle DST via `TimeZoneInfo` (.NET) / `java.time.ZoneId` (Android).
- A **slot** = one tick, id like `2026-10-05T09:00`. On a tick, a device tries to create `users/{uid}/slots/{slotId}` with the picked photo using the Firestore "create only if not exists" precondition (REST: `currentDocument.exists=false`).
  Success: it picks and applies. Already exists: read the photo and apply it. Clean up old slots after a few days.
- Any signed-in device can claim a slot (phone keeps changing even if the PC is off).
- Android scheduled ticks: `AlarmManager` exact alarm (needs "Alarms & reminders" permission on 12+), falling back to WorkManager.

## Change now

- Syncs to the other device. Two presses within seconds: **latest write wins**, fine.
- Writes `users/{uid}/current`, then notifies other devices.
- Near-instant delivery without paying: **FCM data message (high priority) sent by a free Cloudflare Worker**
  (free tier, no card). Windows calls the Worker with its Firebase ID token; the Worker verifies it, reads the user's
  device FCM tokens from `users/{uid}/devices/{deviceId}`, and sends the push. Service account key lives only as a Worker secret;
  rate-limit per uid. Cloud Functions are out (needs Blaze).
- Backup: Android **WorkManager poll every ~15 min** of one tiny doc (~96 reads/day). Also re-check on app open.
  Optional foreground-service listener is NOT the default (persistent notification + battery).
- OEM battery killers (Xiaomi, Huawei, Samsung) can still delay pushes; the poll covers that.

## Firestore / rules notes

- Current `website/firestore.rules` only allows the `users/{uid}` doc and `sessions`. Add owner-only rules for new
  subcollections: settings, current, history, interests, slots, devices.
- Windows talks to Firestore over **REST** (`windows/src/Oriel.Core/Auth/FirebaseClient.cs`, project `oriel-gcp`); there is no .NET desktop SDK.
- Poll a single small doc, not the whole history, to stay inside Spark quotas.
- Unsplash key is shared and rate-limited (50 req/h on demo): first-device-wins means one pick per slot, which helps.
  Respect Unsplash terms (hotlink URLs, ping the download tracking endpoint).

## Planned order of work

1. Write the plan: Firestore schema, rules, Worker, details above.
2. **Windows sync first** (riskiest, testable without Android): data model, REST sync, slots, first-sign-in upload, rename Daily Mix to Interests.
3. Cloudflare Worker + FCM.
4. Android app: sign-in + wallpaper setter, then Home, Interests, History, Collections, Settings.
