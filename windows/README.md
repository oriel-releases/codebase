# Oriel

A small native Windows 11 app that gives you a fresh, high-quality Unsplash photo as your desktop wallpaper every day.

WinUI 3 (Windows App SDK) + C#, Fluent design, Mica, follows your light/dark theme. Lives in the system tray.

Light in the background: closing the window destroys it and gives the memory back, and the tray process
runs in Windows efficiency mode. The scheduler sleeps until the next change is due instead of polling.

## Features

- Daily wallpaper at a time you pick, plus at sign-in if today's isn't set yet. Also a **Change now** button.
- Topics (nature, space, cities, …) as chips, your own search terms, and excluded keywords.
- Never repeats a photo (every photo ID is stored in SQLite).
- Quality filters: landscape only, at least your screen resolution, optional minimum likes.
- Multi-monitor: same image everywhere or a different one per monitor. Fit: Fill / Fit / Stretch / Tile / Center / Span.
- History with photographer credit, re-apply, favorite, delete and open-in-folder. Non-favorites are cleaned up after N days.
- Sets every virtual desktop, not just the current one (Windows 11 24H2+).
- Tray icon (Change now / Open / Pause / Quit). Closing the window keeps it running in the tray.
- Start with Windows, plus a Task Scheduler fallback so the daily change happens even when the app is closed.
- Optional toast with the photo and Keep / Skip / Favorite buttons.
- Offline or API errors: your current wallpaper is kept, it retries with backoff (2m → 5m → 15m → 30m → 1h), and an InfoBar tells you what happened.

## Requirements

- Windows 10 1904x / Windows 11
- .NET 10 SDK (to build). Visual Studio is **not** required; everything builds with the `dotnet` CLI.
- An Unsplash access key at build time (see below). End users never enter one.

## Unsplash key (build time)

Oriel ships with its own Unsplash access key, so users don't need one. The key is baked into
`Oriel.Core.dll` at build time from, in order:

1. the `UNSPLASH_ACCESS_KEY` environment variable (handy for CI), or
2. `UNSPLASH_ACCESS_KEY=...` in `windows\.env` (git-ignored).

The build warns if neither is set; the app then builds but can't fetch photos.
Get a key at <https://unsplash.com/developers> → **Your apps** → **New Application** (use the **Access Key**, not the Secret key).

Every install shares this one key, so its rate limit is shared too: demo apps get 50 requests/hour,
and one change uses about 2 (one search + one download-tracking ping). Apply for production access
in the Unsplash dashboard before shipping widely. The key is extractable from the binary, like any
key embedded in a desktop app; it's a public access key, not the secret.

## Build & run

```powershell
dotnet build Oriel.slnx -p:Platform=x64
dotnet test tests\Oriel.Core.Tests
dotnet run --project src\Oriel.App -p:Platform=x64
```

## Release (installer + auto-update)

Oriel ships as `Oriel-Setup.exe` (built with [Velopack](https://velopack.io)) from GitHub Releases at
<https://github.com/akbarjorayev/oriel-public/releases>, a public repo that only holds releases
(this source repo is private; the updater must read releases without a token). The installer is per-user (no admin) and puts the
app in `%LOCALAPPDATA%\OrielApp`. Settings and history stay in `%LOCALAPPDATA%\Oriel` and survive
updates and uninstall.

Installed copies check for updates a minute after start and then daily, download in the background,
and install when the user clicks **Settings > Updates > Restart to update** or quits Oriel.

To publish a release:

1. Bump `<Version>` in `src\Oriel.App\Oriel.App.csproj` (e.g. `1.0.1`). Quit any Oriel running from `artifacts\`.
2. Create a GitHub token with **Contents: read and write** on `oriel-public` (fine-grained token), then:
   ```powershell
   $env:GITHUB_TOKEN = '<token>'
   .\scripts
elease.ps1          # publish -> vpk pack -> upload to GitHub Releases as v<version>
   ```
   `-NoUpload` builds `releases\` locally without publishing.
3. Update `version` and `releasedOn` in `website\src\config
elease.ts` and redeploy the site.

Notes:

- Never change the Velopack pack id (`OrielApp` in `release.ps1`) or existing installs stop updating.
- Releases must be normal (not pre-release) so `releases/latest` and the updater see them.
- Copies installed from the old zip / `install.ps1` don't self-update; reinstall with `Oriel-Setup.exe`.
- The installer is unsigned for now, so SmartScreen warns on first run. To sign, pass
  `-AzureSignFile <metadata.json>` (Azure Trusted Signing).
- Remove the `Stage` `AssemblyMetadata` in the csproj (and `stage` in the website's `site.ts`) when Oriel leaves beta.

## Install from source (per user, no admin)

```powershell
.\scripts\publish.ps1      # self-contained Release build -> artifacts\Oriel-win-x64
.\scripts\install.ps1      # copies to %LOCALAPPDATA%\Programs\Oriel, adds a Start menu shortcut, starts it
.\scripts\uninstall.ps1    # removes app, Run key, scheduled tasks (add -KeepData to keep settings/history)
```

On start, the app points the Run key and the scheduled task at whichever `Oriel.exe` is running. So after installing, launch the installed copy once (the install script does this for you).

## Command line

`Oriel.exe` is a GUI app, so in a terminal wait for it like this:
`Start-Process .\Oriel.exe -ArgumentList '--status' -Wait -NoNewWindow`

| Flag | What it does |
| --- | --- |
| `--apply-today` | Change the wallpaper if today's isn't set yet (used by Task Scheduler) |
| `--change-now` | Change it right now |
| `--status` | Show settings, monitors, last run |
| `--background` | Start in the tray without opening the window (used at sign-in) |

Exit codes: `0` ok, `1` failed (wallpaper kept), `2` bad usage, `3` built-in key missing or rejected by Unsplash.

## Where things live

| What | Where |
| --- | --- |
| Wallpapers | `Pictures\Oriel` (follows OneDrive folder redirection if you use it) |
| Settings / state | `%LOCALAPPDATA%\Oriel\settings.json`, `state.json` |
| History DB | `%LOCALAPPDATA%\Oriel\oriel.db` |
| Logs | `%LOCALAPPDATA%\Oriel\logs` |
| Startup | `HKCU\...\Run\Oriel`, Task Scheduler tasks `Oriel\ApplyToday` and `Oriel\Retry` (fallback when the app is closed) |

## Project layout

```
src/Oriel.Core          providers, wallpaper setter, storage, scheduling (no UI, unit-tested)
  Providers/               IImageProvider + UnsplashProvider: add Pexels/Bing/Wikimedia here
  Wallpaper/               SystemParametersInfo (all monitors), IDesktopWallpaper (per monitor), virtual desktops
  Services/                WallpaperService (pick → download → set → record), retention, filters
  Scheduling/              in-app scheduler, Run key, Task Scheduler fallback, settings sync
  Storage/ Settings/ Security/
src/Oriel.App           WinUI 3 app (namespace Oriel.UI): MVVM pages, tray, toasts, CLI
tests/Oriel.Core.Tests  xUnit, with mocked HTTP and wallpaper setter
```

### Adding another provider

Implement `IImageProvider` (random candidates, download at a size, usage tracking) and register it in
`ServiceCollectionExtensions.AddOrielCore`. `WallpaperService` doesn't know about Unsplash.

## Design notes

- **Unpackaged, not MSIX.** MSIX needs a signing certificate, and it makes the CLI mode, Task Scheduler and Run key awkward. The app is self-contained, so the Windows App SDK runtime ships with it.
- **Downloads happen before the desktop changes.** If anything fails halfway, nothing on screen changes.
- **Deleted photos stay in the DB** (soft delete), so "never repeat" still holds after cleanup.
- **Excluded keywords are best effort.** Unsplash's random endpoint can't exclude terms, so Oriel checks the photo descriptions.

Photos are provided by [Unsplash](https://unsplash.com/?utm_source=oriel&utm_medium=referral) and credited to their photographers, as the [API guidelines](https://help.unsplash.com/en/articles/2511245-unsplash-api-guidelines) require.
