# Oriel: Android + cross-device sync plan

## Repositories

- **Private, source code:** https://github.com/akbarjorayev/oriel (`origin`; the whole repo root is pushed here, branches like `release-1.0.3`, default `main`).
- **Public, releases only:** https://github.com/akbarjorayev/oriel-public (installers / Velopack releases are published here so the updater can read them without a token).

Status: **decided, not built yet.** Nothing below is implemented. Windows app is in `windows/`, website in `website/`.
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
