/** Copy for the privacy policy. Keep it in step with what the apps and the extension actually store. */

export const privacyPage = {
  title: "Privacy policy",
  updatedOn: "2026-11-07",
  intro:
    "Oriel (the Windows, Linux and Android apps, the browser extension and this website) is built to keep as little about you as possible. This page says what is stored, where, and how to remove it.",
  sections: [
    {
      title: "What Oriel stores",
      body: [
        "When you sign in with Google, we receive your name, email address and profile picture from Google. When you sign up with your email instead, we keep the name and email address you enter; your password is handled by Google's Firebase Authentication and never stored by Oriel. They are saved in your account document so the website can show who is signed in.",
        "For every app or browser that is signed in, we keep a small session record: the platform (Windows, Linux or extension), a device name and the time of the last activity. This is what the Devices list on your account page shows.",
        "When a desktop app changes your wallpaper, it saves the Unsplash photo ID, its links, the photographer's name, the topic and a color picked from the photo, so the extension (and your other devices) can show the same photo. We never store the image itself.",
        "When the extension is signed in, its settings (topics, frequency, clock, quote, search and the other display options) and the new tab shortcuts you add (their names and web addresses) are saved to your account, so every browser you sign in to shows the same. Signing out erases the shortcuts from that browser.",
        "If you share your wallpaper with a code, the code and your first name are saved. People who follow you with it can see the photo on your desktop (the same Unsplash details as above, nothing else), and you can see how many follow you. Stop sharing and the code stops working for everyone. When you follow a friend, your first name is saved in their list of followers until you unfollow.",
        "When you set an AI wallpaper (made on the AI page), its public picture link (on media.pollinations.ai) and the words you typed are saved like a photo, so your other devices can show it too. If you star it, it is saved with your favorites.",
        "A share link you send carries the Unsplash photo, the photographer's name and your first name, so your friend sees who sent it.",
        "The desktop apps add 1 to a few anonymous daily totals: new installs, devices in use that day, devices still in use after 7 and 30 days, and wallpapers shared, opened and set. They are plain numbers per day, with no account, device or IP address attached, and they help us see whether Oriel works for people.",
      ],
    },
    {
      title: "What stays on your device",
      body: [
        "Without an account, the extension's settings and shortcuts are kept only in your browser and are not sent to us.",
        "The sign-in handoff from this website to the extension is kept in the browser's extension storage and removed as soon as the extension has used it.",
        "The desktop apps keep their settings, history and downloaded wallpapers on your computer.",
      ],
    },
    {
      title: "What we do not do",
      body: [
        "No advertising, tracking or fingerprinting, and no analytics beyond the anonymous daily totals described above: nothing about which buttons you press or what you look at. We do not sell or share your data, and we do not use it for anything except running Oriel.",
        "The extension does not read, record or send your browsing history or the pages you open, and Oriel keeps no search history: nothing you search for is saved, in your browser or on our servers.",
        "Its search box (off until you turn it on) hands what you search for to your browser's own default search engine. Two settings, each on by default (Settings, Search), also send what you type there, as you type, straight from your browser: Google suggestions to Google, and Wikipedia pages to Wikipedia (a couple of matching pages with a picture). It is not sent to Oriel, is not saved, and is not linked to your account. Turn either one off and what you type goes there no more; with both off, nothing is sent until you press Enter.",
        "A photo you paste, drop or pick for the search box is kept in the tab's memory only, and is gone when you remove it or close the tab. It leaves your device only when you search: then the photo, with any words you added, is sent to Google Lens to find results, under Google's privacy policy. Oriel does not receive or keep it.",
        "Only when you click the extension's toolbar icon does it look at the address and title of the tab you're on, to offer adding it as a shortcut. Nothing is kept unless you add it.",
      ],
    },
    {
      title: "Services we rely on",
      body: [
        "Google Firebase (Authentication and Firestore) stores your account and sync data, under Google's privacy policy. Each account's data can only be read by that account, except the current wallpaper, which people you let follow you can read.",
        "Unsplash provides the photos. Your browser or app loads images directly from images.unsplash.com, so Unsplash sees the request, like any website you visit, under Unsplash's privacy policy.",
        "Pollinations makes AI wallpapers. When you create one, the words you type (and a fuller description of them written by Pollinations' text model) are sent to Pollinations, which draws the picture and keeps it at a public, unlisted link for about 30 days after it was last opened, under Pollinations' terms. No account details are sent with them. Don't type anything private there.",
        "Google also provides the extension's search suggestions and, when you search with a photo, Google Lens; Wikipedia (the Wikimedia Foundation) provides the pictures and short descriptions in the suggestions, under its privacy policy. Both see the text you type, or the photo you search with, like any website you visit; neither gets your Oriel account details.",
        "Shortcut icons come from your browser's own icon cache; when it doesn't have one, the icon is loaded from that website itself (no icon service in between).",
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
