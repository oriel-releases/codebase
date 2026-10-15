/** Copy for the download page. */

export const downloadPage = {
  title: "Download Oriel",
  /** Small facts under the download button. */
  promises: ["Free, no ads", "Updates itself", "Sign in with Google or email"],
  installSteps: {
    windows: [
      "Open Oriel-Setup.exe. It installs in seconds, no admin needed.",
      "If Windows says it protected your PC, click More info, then Run anyway.",
      "Oriel starts in your tray and keeps itself up to date.",
    ],
    macos: [
      "Open Oriel.dmg and drag Oriel into your Applications folder.",
      "Open Oriel. If macOS says it can't check it, go to System Settings, Privacy & Security, and click Open Anyway.",
      "Oriel starts in your menu bar and keeps itself up to date.",
    ],
    android: [
      "Open Oriel.apk. If Android asks, allow your browser to install apps.",
      "Sign in with Google or email: your phone shows the same wallpaper as your computer.",
      "Oriel changes your wallpaper on schedule and keeps itself up to date.",
    ],
    linux: [
      "Make Oriel.AppImage executable (right-click, Properties, Allow executing) and open it.",
      "No install and no root. Oriel adds itself to your app menu and starts in your tray.",
      "It keeps itself up to date, and the AppImage updates in place.",
    ],
  },
  unsupportedNotice: "Oriel runs on Windows, Mac, Linux and Android. Open this page on one of them to download it.",
  extension: {
    title: "Also for your browser",
  },
  /** The photo in the preview: an Unsplash photo hotlinked from Unsplash's own CDN, credited on the image. */
  preview: {
    id: "IHKBF23A_iw",
    path: "photo-1476990789491-712b869b91a5",
    photographer: "v2osk",
    handle: "v2osk",
    alt: "A winding road through green hills and rocky ridges on the Isle of Skye",
  },
} as const;
