# Oriel for Android

The Oriel app for Android phones: a new Unsplash wallpaper on a schedule, on the home screen, lock screen or both.
Same account as the Windows and Linux apps, and optionally the same photo as your computer.

Native Kotlin + Jetpack Compose. Android 8.0 (API 26) and newer.

## Lightweight by design

- **No background service, no polling by default.** One alarm is set for the next change; when it fires, a
  JobScheduler job (expedited, network required) does the work and exits. Nothing runs in between.
- **No exact-alarm permission.** Changes use an inexact alarm allowed in Doze, so they land within a few minutes of
  the time, and no special permission is asked for.
- **Following a computer or a friend** is the only periodic work: a job every ~30 min, only with a network and when the
  battery isn't low, reading one tiny Firestore document. A photo is downloaded only when it actually changed.
- **No decoding on the phone for the wallpaper.** Unsplash's CDN sends a JPEG already cropped to this screen
  (`w=…&h=…&fit=crop&crop=faces,entropy`); it is streamed to a temporary file, handed to `WallpaperManager`, and deleted.
- **Small dependencies.** No Firebase SDK, no Google Play services, no HTTP or image library: Firebase Auth/Firestore
  and Unsplash are plain REST calls (`HttpURLConnection` + `org.json`, like the Windows app's `FirebaseClient.cs`).
  History is the platform's SQLite. Thumbnails are small WebP at their display size, cached on disk by
  `HttpResponseCache` and in memory as RGB_565 bitmaps.
- **No files kept.** History stores metadata only; re-applying fetches the photo again from the CDN.

## Features

- **Home**: current wallpaper (cropped to this phone's shape), credit, next change, **Change now** (twice an hour,
  because the Unsplash key is shared by every install).
- **Search**: Unsplash search with suggestions, portrait photos big enough for the screen; set one right away.
- **Interests** (inside Search, like the desktop apps): the built-in topic chips, and your own words, which come from a
  search ("Add to Interests"). Each change picks one at random.
- **Friends**: following a friend by code, sharing your wallpaper with your own code (link and copy), your followers.
- **History**: every photo set on this phone, favorites, delete (a deleted photo is never picked again).
- **Settings**: daily time or every 1/3/6/12 hours, anchored in an IANA time zone (same moments on every device);
  home/lock/both; favorites only; popular photos only; pause.
- **Same wallpaper on every device**: a change on any device of the account (phone, computer, browser extension) shows
  here too, within seconds while Oriel is open (the Realtime Database doorbell `pulse/{uid}`), else on open and every
  15 min. Following a friend shows their wallpaper on all your devices.
- Share links (`oriel://wallpaper?id=…&from=…`) open a preview with **Set as wallpaper**.
- After each change of its own, the phone publishes `sync/current` with `source: "android"`, so the browser extension
  and followers see it too. (Not while following: that photo came from the other device.)

## Sign-in

Same as the desktop apps: **Sign in with Google** opens `https://oriel.akbarjon.uz/login` in the browser; after
Google sign-in the website opens `oriel://auth?token=…`, which this app registers. The Google ID token becomes the
app's own Firebase session (refresh token encrypted with an Android Keystore key) plus a
`users/{uid}/sessions/{id}` document with `platform: "android"`. Ending that session on `/me` signs the phone out.

No Google Cloud OAuth client or SHA-1 registration is needed for this flow.

## Build

Needs the Android SDK (platform 36) and a **JDK 17–24** to run Gradle 8.14 (Android Studio's bundled JBR 25 is too new
for it; any JDK 17 works).

The Unsplash access key is baked in at build time from `UNSPLASH_ACCESS_KEY` (environment), else `android/.env`,
else `windows/.env` (all git-ignored).

```powershell
cd android
$env:JAVA_HOME = "$HOME\.jdks\ms-17.0.20.1"   # or any JDK 17
.\gradlew assembleDebug                       # app/build/outputs/apk/debug/app-debug.apk (id uz.akbarjon.oriel.dev)
.\gradlew installStaging                      # onto a connected phone: debug's id and key, but optimized (use this)
.\gradlew installDebug                        # only when you need the debugger (much slower UI)
```

Release: put `storeFile`, `storePassword`, `keyAlias`, `keyPassword` in `android/keystore.properties` (git-ignored),
then `.\gradlew assembleRelease` (R8 minified and resource-shrunk). Never change `applicationId`
(`uz.akbarjon.oriel`) once published.

## Firestore

The phone writes `sync/current` with `source: "android"`: `website/firestore.rules` allows it; deploy the rules
(`firebase deploy --only firestore:rules` in `website/`) before releasing.

## Not built yet (from the sync plan in the root CLAUDE.md)

Interests/history/settings sync, first-device-wins slots, and push via the Cloudflare Worker + FCM need the Windows
side first (step 2 of the plan). Until then, "Same as my computer" gives the shared wallpaper with one small read
every ~30 min and on app open.
