# Oriel browser extension

A new tab page with an Unsplash photo, a clock, a quote and an optional search bar. Works in Chrome, Edge, Brave, Opera (Chromium) and Firefox from one codebase. Plain TypeScript + Tailwind 4, built with Vite.

Two ways to pick the photo, chosen in the extension's own settings (the gear on the new tab):

- **Match my desktop** (needs an Oriel account): follows the wallpaper that Oriel for Windows / Linux set last, live while the tab is open.
- **Its own**: picks from `src/data/pool.json` (396 curated photos, 14 topics) by topic and frequency (every tab / hourly / daily). Works signed out, with no API key and no API calls.

## How the sync works

```
Oriel desktop ──applies wallpaper──▶ users/{uid}/sync/current (Firestore, owner-only)
                                              │ onSnapshot (live while a new tab is open)
Extension new tab ◀───────────────────────────┘
```

- The desktop apps publish only the Unsplash photo (id, links, photographer), never the image. The extension builds its own crop for the browser window.
- No polling and no background page: a new tab reads the document when it opens and listens while it stays open. Nothing runs when no tab is open, so there is no battery cost and no 15 minute lag (that was for the phone, which can't keep a listener open).
- Linking is optional and per browser. The mode lives in this browser's `localStorage`; the extension is the only place it is set.

## Sign-in

Reuses the website's Google sign-in, like the desktop apps do:

1. Settings → *Connect Oriel account* opens `https://oriel.akbarjon.uz/login?for=extension`.
2. After Google sign-in, the page `postMessage`s the credential; `src/content/bridge.ts` (a content script that only runs on `/login`) puts it in `chrome.storage.local`.
3. The new tab page signs in to Firebase with it (`firebase/auth/web-extension`) and creates a `users/{uid}/sessions/{id}` document with `platform: "extension"`. Deleting that session from anywhere signs the browser out within 6 hours.

## Develop

```
npm install
npm run build          # dist/chrome and dist/firefox
npm run dev            # rebuilds dist/chrome on change
npm run pool           # refresh src/data/pool.json (uses UNSPLASH_ACCESS_KEY from ../windows/.env; 14 API requests)
npm run typecheck
```

- Chrome / Edge: `chrome://extensions` → Developer mode → *Load unpacked* → `dist/chrome`.
- Firefox: `about:debugging` → *This Firefox* → *Load Temporary Add-on* → `dist/firefox/manifest.json`.
- To test sign-in against a local website (`npm run dev` in `website/`), run `localStorage.setItem("oriel.site", "http://localhost:3000")` in the new tab's console.

## Publishing

Zip the *contents* of `dist/chrome` for the Chrome Web Store / Edge Add-ons, and of `dist/firefox` for addons.mozilla.org. The Firebase web config in `src/lib/config.ts` is public by design; access is enforced by Firebase Auth and `website/firestore.rules`.
