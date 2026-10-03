# Oriel - build me a daily wallpaper app for Windows 11

I'm Akbar, a junior+ full-stack dev (Node/NestJS, React/Next.js, some Python). I want a small desktop app called **Oriel** that changes my Windows desktop wallpaper to a fresh, random, high-quality photo every day. Windows Spotlight gives me too few images and repeats them.

## Hard requirement: it must look and feel NATIVE Windows 11
- Use **WinUI 3 (Windows App SDK) with C#** and Fluent Design. No Electron, no web-view UI, no custom-styled fake controls.
- Use real Windows 11 controls: `TextBox`, `PasswordBox`, `NumberBox`, `ComboBox`, `ToggleSwitch`, `ToggleButton`, `Slider`, `NavigationView`, `InfoBar`, `ProgressRing`, `ContentDialog`, `Expander`, `SettingsCard`-style rows (like Windows Settings).
- Mica backdrop, rounded corners, system accent color, follows system light/dark theme automatically.
- Inputs must behave like Windows 11 inputs (the rounded text box with the bottom accent line on focus, standard keyboard + narrator/accessibility support).
- Layout should feel like the Windows 11 Settings app: left NavigationView (Home, Collections, History, Settings), clean card rows.
- If WinUI 3 is a real blocker, tell me before switching to anything else (WPF + WPF-UI is the only acceptable fallback).

## What the app does
1. **Image source**: Unsplash API (`/photos/random`, landscape orientation, `w`/`h` matched to my primary monitor resolution, request the `raw` or `full` URL and add `&w=...&q=90`). Design the image-provider layer behind an interface so I can add Pexels / Bing / Wikimedia Commons later.
2. **Daily change**: new wallpaper once per day at a time I choose (default: at login if today's hasn't been set yet, otherwise at the chosen time). Also a "Change now" button.
3. **Topics / collections**: I can pick topics (nature, space, cities, architecture, minimal, mountains, ocean, dark, abstract, etc.) as chips or checkboxes, add custom search queries, and exclude topics. Each day picks randomly among my enabled topics.
4. **No repeats**: store IDs of every image used in a local SQLite DB and never reuse one.
5. **Quality filter**: skip images smaller than my screen resolution, skip portrait images, optionally skip images with low like count.
6. **Multi-monitor**: option for same image on all monitors or different image per monitor (use `IDesktopWallpaper` COM API). Fit mode selector: Fill / Fit / Stretch / Tile / Center / Span.
7. **History page**: grid of past wallpapers with thumbnail, photographer name (clickable link to their Unsplash profile, required by Unsplash API guidelines), date, buttons: Re-apply, Favorite, Delete, Open in folder.
8. **Favorites**: a favorited image is kept forever; non-favorites are auto-deleted after N days (setting, default 30).
9. **Tray icon**: runs in the background in the system tray, right-click menu: Change now, Open Oriel, Pause, Quit. Closing the window minimizes to tray.
10. **Start with Windows**: toggle in Settings (use `StartupTask` for packaged app, or registry Run key if unpackaged).
11. **Notifications**: optional native Windows toast when the wallpaper changes, with photo thumbnail + photographer + "Keep / Skip / Favorite" actions.
12. **Offline / failure handling**: if the API fails or I'm offline, keep the current wallpaper, retry later with backoff, and show an `InfoBar` explaining what happened. Never leave me with a blank desktop.
13. **Unsplash API key**: entered in Settings via a `PasswordBox`, stored securely (Windows Credential Locker / DPAPI), never hardcoded, never logged. Show a short "how to get a free key" link with steps. Respect rate limits (50 req/hour on demo apps) and trigger the Unsplash download-tracking endpoint when an image is used.

## Architecture / code quality
- MVVM with CommunityToolkit.Mvvm, dependency injection via Microsoft.Extensions.DependencyInjection/Hosting.
- Layers: `Oriel.Core` (providers, scheduler, wallpaper service, storage), `Oriel.App` (WinUI 3 UI). Core should be unit-testable (xUnit) with the HTTP client and wallpaper setter mocked.
- Wallpaper setting via `SystemParametersInfo(SPI_SETDESKWALLPAPER)` for simple mode and `IDesktopWallpaper` for per-monitor. Save the image as JPEG in `%USERPROFILE%\Pictures\Oriel`.
- Scheduling: a lightweight in-app scheduler + a Windows Task Scheduler fallback so it still works if the app isn't running at the chosen time (a tiny CLI mode: `Oriel.exe --apply-today`).
- Settings in a JSON file under `%LOCALAPPDATA%\Oriel`.
- Package as MSIX (or unpackaged self-contained .exe with an installer script) and add a `README.md` with build/run steps.

## How I want you to work
1. First, check what's installed (.NET SDK, Visual Studio / Windows App SDK templates, Windows SDK). Tell me exactly what I need to install if something is missing. Don't assume.
2. Give me a short plan and the folder structure, then scaffold the solution.
3. Build in this order: Core (provider, wallpaper setter, storage) -> CLI `--apply-today` that works end to end -> WinUI shell + NavigationView -> Settings -> Home -> History -> tray + startup + toasts.
4. Run and test after each step. I want to see the wallpaper actually change before you build UI around it.
5. Keep explanations short and casual, no walls of text. Tell me bluntly if one of my requirements is a bad idea.
6. Commit in small steps with clear messages.

Start now.
