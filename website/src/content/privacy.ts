/** Copy for the privacy policy. Keep it in step with what the apps and the extension actually store. */

export const privacyPage = {
  title: "Privacy policy",
  updatedOn: "2026-10-07",
  intro:
    "Oriel (the Windows and Linux apps, the browser extension and this website) is built to keep as little about you as possible. This page says what is stored, where, and how to remove it.",
  sections: [
    {
      title: "What Oriel stores",
      body: [
        "When you sign in with Google, we receive your name, email address and profile picture from Google. They are saved in your account document so the website can show who is signed in.",
        "For every app or browser that is signed in, we keep a small session record: the platform (Windows, Linux or extension), a device name and the time of the last activity. This is what the Devices list on your account page shows.",
        "When a desktop app changes your wallpaper, it saves the Unsplash photo ID, its links, the photographer's name and the topic, so the extension can show the same photo. We never store the image itself.",
      ],
    },
    {
      title: "What stays on your device",
      body: [
        "Extension settings (topic, frequency, clock, quote and search options) are kept in your browser and are not sent to us.",
        "The sign-in handoff from this website to the extension is kept in the browser's extension storage and removed as soon as the extension has used it.",
        "The desktop apps keep their settings, history and downloaded wallpapers on your computer.",
      ],
    },
    {
      title: "What we do not do",
      body: [
        "No analytics, advertising, tracking or fingerprinting. We do not sell or share your data, and we do not use it for anything except running Oriel.",
        "The extension does not read, record or send your browsing history, the pages you open or what you type in its search box. Search goes to your browser's own default search engine.",
      ],
    },
    {
      title: "Services we rely on",
      body: [
        "Google Firebase (Authentication and Firestore) stores your account and sync data, under Google's privacy policy. Each account's data can only be read by that account.",
        "Unsplash provides the photos. Your browser or app loads images directly from images.unsplash.com, so Unsplash sees the request, like any website you visit, under Unsplash's privacy policy.",
        "Telegram is used only if you contact support through the support bot.",
      ],
    },
    {
      title: "Your choices",
      body: [
        "You can use the extension without an account: it then picks from a built-in set of photos and sends nothing to us.",
        "Sign out of any app or browser from the Oriel account page, and its session is removed. Signing out of the extension also removes its stored sign-in.",
        "To delete your account and everything stored about it, message the support bot and we will remove it.",
      ],
    },
    {
      title: "Children",
      body: ["Oriel is not directed at children under 13, and we do not knowingly collect their data."],
    },
    {
      title: "Changes",
      body: ["If this policy changes, the new version is published here with a new date."],
    },
  ],
} as const;
