/** Copy for the download page. */

export const downloadPage = {
  title: "Download Oriel",
  installSteps: {
    windows: [
      "Open Oriel-Setup.exe. It installs in seconds, no admin needed.",
      "If Windows says it protected your PC, click More info, then Run anyway.",
      "Oriel starts in your tray and keeps itself up to date.",
    ],
    linux: [
      "Make Oriel.AppImage executable (right-click, Properties, Allow executing) and open it.",
      "No install and no root. Oriel adds itself to your app menu and starts in your tray.",
      "It keeps itself up to date, and the AppImage updates in place.",
    ],
  },
  unsupportedNotice: "Oriel runs on Windows and Linux. Open this page on your computer to download it.",
} as const;
