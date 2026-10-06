# Oriel for Linux

A new Unsplash wallpaper on your desktop every day. Same app and the same features as the Windows version
(`../windows`), built for Linux: C# on .NET 10 with [Avalonia](https://avaloniaui.net). It lives in the tray, keeps
nothing running between changes, and gives its memory back when you close the window.

## Features

Everything the Windows app has, adapted to how Linux works:

| Feature | On Linux |
| --- | --- |
| Daily change at a time you pick, slideshow (every 1/3/6/12 h), change at login, **Change now** | In-app scheduler that sleeps until the next change, plus a **systemd user timer** (`oriel-apply.timer`, `Persistent=true`) that runs `oriel --apply-today` when the app isn't open and catches up after sleep or a powered-off PC |
| Topics ("Daily mix"), your own searches, Search page, set any result as wallpaper | Same |
| Never repeats a photo, quality filters (landscape, screen size, minimum likes) | Same (SQLite history) |
| History, Favorites, re-apply, delete, open in folder, 30-day cleanup | Same. Thumbnails are loaded only while visible; "open in folder" selects the file in your file manager |
| Tray icon (Next wallpaper / Open / Pause / Quit) | StatusNotifierItem (KDE, XFCE, Cinnamon, GNOME with the AppIndicator extension) |
| Global shortcut (default `Ctrl+Alt+W`) | X11: grabbed directly. Wayland doesn't allow it; bind a key in your system settings to `oriel --change-now` |
| Fit modes, multi-monitor | Fill / Fit / Stretch / Tile / Center / Span, mapped to each desktop's own options. Monitor sizes from `xrandr`, `swaymsg` or `hyprctl` |
| Lock screen | KDE Plasma: set directly. GNOME and Cinnamon already show your wallpaper there. Other desktops don't allow it |
| Start with the system | XDG autostart entry (works on every desktop) |
| Sign in with Google | Website login, then the `oriel://` link opens the app (registered as a URL handler on first start). Tokens go to the keyring (`secret-tool`), else a private file |
| Self-update | [Velopack](https://velopack.io) AppImage from <https://github.com/oriel-releases/linux/releases> |
| Retry with backoff when offline, error banner, notification | Same (`notify-send` for notifications) |
| Optional sync, instant messages between devices | Not built yet on any platform (see `../CLAUDE.md`) |

### Wallpaper support

| Desktop | How it's set |
| --- | --- |
| GNOME, Budgie, Unity, Pantheon | `gsettings` (`picture-uri` and `picture-uri-dark`) |
| Cinnamon, MATE, Deepin | `gsettings` of their own schema |
| KDE Plasma | `plasma-apply-wallpaperimage` (older Plasma: `qdbus` script) |
| Xfce | `xfconf-query`, every monitor and workspace |
| Sway | `swaymsg output * bg` |
| Hyprland | `swww img`, else `hyprctl hyprpaper` |
| LXQt, LXDE | `pcmanfm-qt` / `pcmanfm --set-wallpaper` |
| i3, bspwm, openbox, ... | `feh`, `nitrogen`, `xwallpaper` or `hsetroot`, whichever is installed |

## Performance

- **Idle is nothing.** No polling. The scheduler sleeps until the next change (at most 10 minutes, to notice sleep/resume
  and clock changes); the single-instance listener, the tray and the shortcut grab all wait in the kernel.
- **Closing the window frees it.** The window is destroyed, thumbnails and previews are disposed, the GC compacts and
  `malloc_trim` returns the heap to the OS.
- **Software rendering by default** (no GL driver loaded, no GPU wakeups). `ORIEL_GPU=1 oriel` switches to the GPU.
- **Pages are created when shown and thrown away when left.** History and search use virtualized lists whose
  thumbnails are decoded to ~320 px straight from disk and released when scrolled away.
- **Short-lived work is nice'd.** `oriel --apply-today` (the timer run) runs at lower priority and exits in seconds.
- Runtime trimmed down: invariant globalization (no ICU), no tiered PGO, non-concurrent GC, `GCConserveMemory`,
  ReadyToRun pre-compiled code for a fast start. Compiled XAML bindings (no reflection at run time).

## Build and run

Requirements: .NET 10 SDK. An Unsplash access key is baked in at build time from `UNSPLASH_ACCESS_KEY` or `linux/.env`
(same as Windows; the file is git-ignored).

```bash
dotnet build Oriel.slnx
dotnet test tests/Oriel.Core.Tests
dotnet run --project src/Oriel.App                # the app
dotnet run --project src/Oriel.App -- --status    # headless: what Oriel sees (desktop, monitors, settings)
```

Runtime tools: `xdg-open`, and whatever your desktop needs to set a wallpaper (see the table). Optional: `secret-tool`
(libsecret-tools, for the keyring), `notify-send` (libnotify-bin), `xrandr`.
Avalonia needs `libfontconfig`, `libX11`, `libICE`, `libSM` (present on any desktop install).

## Command line

```
oriel                 open the window (or bring the running one forward)
oriel --background    start in the tray only
oriel --apply-today   change the wallpaper if today's isn't set yet (the systemd timer runs this)
oriel --change-now    change it now (hand this to a keyboard shortcut on Wayland)
oriel --status        settings, desktop, monitors, last error
oriel --version
```

## Release (AppImage + auto-update)

Oriel ships as `Oriel.AppImage` (built with Velopack) from GitHub Releases at
<https://github.com/oriel-releases/linux/releases>, a public releases-only repo (the source repo is private, so the
updater can read releases without a token). Settings and history live in `~/.local/share/oriel` and survive updates.

1. Bump `<Version>` in `src/Oriel.App/Oriel.App.csproj`.
2. Create a GitHub token with **Contents: read and write** on `oriel-releases/linux`, then on Linux (or WSL):
   ```bash
   export GITHUB_TOKEN=...
   ./scripts/release.sh            # publish -> vpk pack -> upload to GitHub Releases as v<version>
   ```
   `--no-upload` builds `releases/` locally without publishing. `arm64` builds the Arm package.
3. Set `version` in the Firestore document **`application/linux`**, and update `fallbackVersion` and `releasedOn` for
   Linux in `website/src/config/release.ts`, then redeploy the site.

Never change the pack id (`OrielApp` in `release.sh`) or existing installs stop updating. Releases must be normal
(not pre-release) so `releases/latest` and the updater see them.

## Install from source (per user, no root)

```bash
./scripts/publish.sh      # self-contained Release build -> artifacts/Oriel-linux-x64
./scripts/install.sh      # copies to ~/.local/share/OrielApp, command: oriel
./scripts/uninstall.sh    # removes timers, autostart, menu entry (--purge also removes settings and history)
```

## Layout

```
src/Oriel.Core    shared logic: Unsplash provider, scheduling, history (SQLite), settings, auth, Linux integration
src/Oriel.App     Avalonia UI, tray, single instance, X11 shortcut, updater, CLI
tests/            unit tests for the core (run on any OS)
scripts/          publish, release, install, uninstall
```

`Oriel.Core` started as a copy of the Windows core; the Windows-only parts (DPAPI, registry, Task Scheduler, WinRT lock
screen, virtual desktops) were replaced by the Linux ones above.
