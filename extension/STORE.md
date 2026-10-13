# Store submission notes

Zip the contents of each `release/` folder, then upload `oriel-chrome-<version>` to the Chrome Web Store, `oriel-edge-<version>` to Edge Add-ons, `oriel-firefox-<version>` to addons.mozilla.org (plus `oriel-source-<version>` when it asks for source; build with `npm install && npm run build:firefox`).

- **Privacy policy URL:** https://oriel.akbarjon.uz/privacy
- **Homepage / support:** https://oriel.akbarjon.uz and https://t.me/oriel_support_bot
- **Category:** Chrome: Lifestyle > Art & design (or Make Chrome Yours > Functionality & UI); Edge: Photos & Media or Personalization; Firefox: Photos, Music & Videos
- **Name:** Oriel
- **Single purpose:** Replace the new tab page with a photo from Unsplash, a clock and a quote, optionally matching the wallpaper of the user's Oriel desktop app.

## Short description (132 characters max)

A beautiful Unsplash photo on every new tab. Match your Oriel desktop wallpaper, or pick your own.

## Long description

```
Oriel turns every new tab into a calm, full-screen photo from Unsplash, with a clock, a short quote and an optional search bar.

TWO WAYS TO PICK THE PHOTO
• Its own: choose the topics you like (nature, architecture, space and more) and how often the photo changes: every tab, hourly or daily. Works right away, with no account.
• Match my desktop: if you use Oriel for Windows or Linux, your new tab shows the same photo as your desktop wallpaper, and changes when it does. This needs a free Oriel account (sign in with Google).

FEATURES
• Hand-picked, high-quality photos, credited to their photographers
• Clock, daily quote and photo info, each of which you can turn off
• Search uses your browser's own default search engine
• Light and quiet: no background page, nothing runs when no new tab is open

PRIVACY
No ads, no analytics, no tracking. Oriel never reads your browsing history or what you type. Signing in is optional. Full policy: https://oriel.akbarjon.uz/privacy

Oriel desktop apps for Windows and Linux are free: https://oriel.akbarjon.uz/download
Photos by Unsplash. Oriel is not affiliated with or endorsed by Unsplash.
```

## Permission justifications

| Permission | Why |
|---|---|
| `storage` | Keeps the one-time sign-in handoff from the Oriel website to the new tab page in extension storage. Removed once used. |
| `search` | The new tab search box sends queries to the browser's own default search engine. |
| Content script on `https://oriel.akbarjon.uz/login*` | Receives the sign-in credential from the Oriel login page when the user clicks "Connect Oriel account". Runs on that one page only. |
| New tab override (`chrome_url_overrides.newtab`) | The extension's purpose. |

No host permissions, no remote code, no background page.

## Data disclosure answers

- **Collected, only if the user signs in:** name, email and profile picture (Google account, via Firebase Authentication); the wallpaper the user's desktop app applied last (Unsplash photo id and credit); a session record (platform, device name, last active).
- **Not collected:** browsing history, page content, keystrokes, location, health or financial data.
- **Not sold, not used for advertising, not used for anything unrelated to the extension's purpose.**
- Firefox: `data_collection_permissions` is `required: ["none"]` in the manifest. Sign-in is optional and only links the user's own account.

## Screenshots

Chrome and Edge want 1280×800 (or 640×400), Chrome also a 440×280 small promo tile. Take them from the new tab page: photo + clock, the settings panel, and the "Match my desktop" state.
