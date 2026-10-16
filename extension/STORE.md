# Store submission notes

Zip the contents of each `release/` folder, then upload `oriel-chrome-<version>` to the Chrome Web Store, `oriel-edge-<version>` to Edge Add-ons, `oriel-firefox-<version>` to addons.mozilla.org (plus `oriel-source-<version>` when it asks for source; build with `npm install && npm run build:firefox`).

- **Privacy policy URL:** https://oriel.akbarjon.uz/privacy
- **Homepage / support:** https://oriel.akbarjon.uz and https://t.me/oriel_support_bot
- **Category:** Chrome: Lifestyle > Art & design (or Make Chrome Yours > Functionality & UI); Edge: Photos & Media or Personalization; Firefox: Photos, Music & Videos
- **Name:** Oriel
- **Single purpose:** Replace the new tab page with a photo from Unsplash, a clock, shortcuts and a quote, optionally matching the wallpaper of the user's other Oriel devices.

## Short description (132 characters max)

A beautiful Unsplash photo on every new tab. Match your Oriel desktop wallpaper, or pick your own.

## Long description

```
Oriel turns every new tab into a calm, full-screen photo from Unsplash, with a clock, your shortcuts, a short quote and an optional search bar.

TWO WAYS TO PICK THE PHOTO
• Its own: choose your interests (nature, architecture, space and more) and how often the photo changes: every tab, hourly or daily. Works right away, with no account.
• My devices: if you use Oriel on Windows, Linux, macOS or Android, your new tab shows the same photo as your wallpaper there, and changes the moment it does. This needs a free Oriel account (Google or email).

FEATURES
• Hand-picked, high-quality photos, credited to their photographers, and a Wallpaper of the day
• Shortcuts to your favorite sites: drag to reorder, add the page you're on from the toolbar icon
• Settings right under the toolbar icon, on any page
• Signed in, your settings and shortcuts are the same in every browser
• Clock, greeting, daily quote, search bar and every button can be turned off
• Search uses your browser's own default search engine

PRIVACY
No ads, no analytics, no tracking. Oriel never reads your browsing history or what you type. Signing in is optional. Full policy: https://oriel.akbarjon.uz/privacy

Oriel apps for Windows, Linux, macOS and Android are free: https://oriel.akbarjon.uz/download
Photos by Unsplash. Oriel is not affiliated with or endorsed by Unsplash.
```

## Permission justifications

| Permission | Why |
|---|---|
| `storage` | Keeps the one-time sign-in handoff from the Oriel website to the new tab page in extension storage. Removed once used. |
| `search` | The new tab search box sends queries to the browser's own default search engine. |
| Content script on `https://oriel.akbarjon.uz/login*` | Receives the sign-in credential from the Oriel login page when the user clicks "Connect Oriel account". Runs on that one page only. |
| `activeTab` | When the user clicks the toolbar icon, the popup reads that one tab's address and title so it can offer "Add to shortcuts". Nothing is read otherwise, and no other tab is ever read. |
| `favicon` (Chrome and Edge only) | Shows each new tab shortcut with the site's icon, from the browser's own icon cache. |
| Toolbar popup (`action.default_popup`) | The settings panel opens under the toolbar icon. |
| Background service worker | Only for the toolbar icon on an Oriel new tab: it tells that page to open its own settings dialog instead of the popup. It does nothing else and makes no network requests. |
| New tab override (`chrome_url_overrides.newtab`) | The extension's purpose. |

No host permissions and no remote code.

## Data disclosure answers

- **Collected, only if the user signs in:** name, email and profile picture (Google account, via Firebase Authentication); the wallpaper the user's desktop app applied last (Unsplash photo id and credit); a session record (platform, device name, last active); the extension's own settings and the new tab shortcuts the user adds (their names and web addresses), so they are the same in every browser of the account. Erased from the browser on sign-out.
- **Not collected:** browsing history, page content, keystrokes, location, health or financial data.
- **Not sold, not used for advertising, not used for anything unrelated to the extension's purpose.**
- Firefox: `data_collection_permissions` is `required: ["none"]` in the manifest. Sign-in is optional and only links the user's own account.

## Screenshots

Chrome and Edge want 1280×800 (or 640×400), Chrome also a 440×280 small promo tile. Take them from the new tab page: photo + clock, the settings panel, and the "Match my desktop" state.
