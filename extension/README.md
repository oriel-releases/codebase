# Oriel browser extension

A new tab page with an Unsplash photo, a clock (or your age, running on: Settings > Clock > Age), a quote and an optional search bar. Works in Chrome, Edge, Brave, Opera (Chromium) and Firefox from one codebase. Plain TypeScript + Tailwind 4, built with Vite.

Two ways to pick the photo, chosen in the extension's own settings (the gear on the new tab):

- **Same as my devices** (needs an Oriel account): shows the wallpaper any device of the account (Windows, Linux, Android) set last, live while the tab is open. The shuffle button here changes it on every device: it publishes a photo of this hour's feed to `sync/current` (`source: "extension"`) and rings the Realtime Database doorbell `pulse/{uid}`.
- **Its own**: picks from the apps' hourly Explore feed (`https://raw.githubusercontent.com/oriel-releases/explore/feed/explore.json`, 300 landscape photos, the same for everyone, new every hour; `src/lib/explore.ts`), every tab / hourly / daily. The feed is kept in `localStorage` and fetched again once the hour turns (at most every 5 minutes). Before the first one arrives it shows the Wallpaper of the day, read from the website's `/today.json` once a day (`src/lib/today.ts`); with neither (just installed and offline), the plain background. No photos are bundled. Works signed out, with no API key and no Unsplash API calls.

## Keyboard

The arrow keys move the focus to the nearest control that way (`src/newtab/keyboard.ts`, the math in `src/lib/spatial.ts`): across the shortcuts, from the search box down to them, through the settings menu and its page. Enter or Space presses it. A dialog takes the focus when it opens, keeps Tab inside it and gives the focus back when it closes.

## Shortcuts and folders

Up to 14 tiles under the clock, synced with the account. Drag a link onto the middle of another tile to make a folder
(or to add it to one); its sides just reorder. A folder (up to 12 links) opens as a card over the page
(`src/newtab/folder-view.ts`): rename it in place, drag links to reorder or out of the card to take them out, Ungroup,
Delete. The editor's Folder choice moves a link without dragging. The list's rules live in `src/lib/shortcuts.ts`
(`combine`, `takeOut`, `ungroup`, `cleanShortcuts`), tested in `tests/`. Version 1.0.4 and older don't know folders: an
edit there uploads the list without them.

## The Age clock

Settings > Clock > Show **Age** puts your age with running decimals where the time was (`src/lib/age.ts`). Signed in,
the birthday is the account's (`users/{uid}.birthday`, set on the website's `/me`; "Set your birthday on Oriel" opens
`/me?set-age=1`, and the new tab reads it again on coming back). Signed out, it is asked here in a small dialog and kept
in this browser only.

## How the sync works

```
Any Oriel device ──sets wallpaper──▶ users/{uid}/sync/current (Firestore, owner-only) + pulse/{uid} (RTDB doorbell)
                                              │ onSnapshot (live while a new tab is open)
Extension new tab ◀───────────────────────────┘
```

- The desktop apps publish only the Unsplash photo (id, links, photographer), never the image. The extension builds its own crop for the browser window.
- No polling and no background page: a new tab reads the document when it opens and listens while it stays open. Nothing runs when no tab is open, so there is no battery cost and no 15 minute lag (that was for the phone, which can't keep a listener open).
- Linking is optional and per browser. The mode lives in this browser's `localStorage`; the extension is the only place it is set.

## Sign-in

Two ways, both under Settings → Account:

- **Email and password** (accounts made on the website with email): signed in right in the panel with
  `signInWithEmailAndPassword`, plus "Forgot password?" (`sendPasswordResetEmail`). The password never leaves the panel.
- **Google** reuses the website's Google sign-in, like the desktop apps do:

1. *Continue with Google* opens `https://oriel.akbarjon.uz/login?for=extension`.
2. After Google sign-in, the page `postMessage`s the credential; `src/content/bridge.ts` (a content script that only runs on `/login`) puts it in `chrome.storage.local`.
3. The new tab page signs in to Firebase with it (`firebase/auth/web-extension`) and creates a `users/{uid}/sessions/{id}` document with `platform: "extension"`. Deleting that session from anywhere signs the browser out within 6 hours.

## Develop

```
npm install
npm run build          # dist/chrome and dist/firefox
npm run dev            # rebuilds dist/chrome on change
npm run typecheck
```

- Chrome / Edge: `chrome://extensions` → Developer mode → *Load unpacked* → `dist/chrome`.
- Firefox: `about:debugging` → *This Firefox* → *Load Temporary Add-on* → `dist/firefox/manifest.json`.
- To test sign-in against a local website (`npm run dev` in `website/`), run `localStorage.setItem("oriel.site", "http://localhost:3000")` in the new tab's console.

## Publishing

`npm run package` makes a store build (no localhost permission) and writes the folders `release/oriel-chrome-<version>` (Chrome Web Store), `release/oriel-edge-<version>` (Edge Add-ons, the same build), `release/oriel-firefox-<version>` (addons.mozilla.org) and `release/oriel-source-<version>` (the source Mozilla asks for); zip the contents of each yourself. Only the newest version stays in `release/`: earlier folders and their zips move to `release/old/<version>/`. Listing text, permission justifications and the privacy answers are in [STORE.md](STORE.md); the policy lives at `/privacy` on the website. Bump `version` in `package.json` for every submission. The Firebase web config in `src/lib/config.ts` is public by design; access is enforced by Firebase Auth and `website/firestore.rules`.
