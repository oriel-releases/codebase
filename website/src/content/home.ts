/** Copy for the home page. UI components only render what's here. */

/** What a home feature card draws above its words (components/home/FeatureVisuals). */
export type FeatureVisual = "devices" | "schedule" | "morning" | "topics" | "history" | "screens" | "quiet" | "browser" | "favorites";
export type Feature = { title: string; body: string; visual?: FeatureVisual };
export type Step = { title: string; body: string };
export type FaqItem = { question: string; answer: string };

export const hero = {
  eyebrow: "Free for Windows, Mac, Linux and Android",
  title: "One photo, every device",
  body: "Oriel quietly sets a beautiful Unsplash photo as your wallpaper each morning, the same one on your computer, your Mac and your phone. Pick the topics you like, and forget about it.",
} as const;

/** The devices under the hero, and "Try yours": a visitor's own photo on them, kept in their browser. */
export const sync = {
  label: "The same photo on an iMac, a MacBook and an iPhone",
  cta: "Try yours",
  another: "Try another",
  reset: "Back to our photos",
  failed: "That file couldn't be opened. Try a JPEG or PNG.",
} as const;

/** The photos the home page's devices cycle through (ids from data/today.json). */
export const showcase = {
  photoIds: [
    "mWRR1xj95hg",
    "uILhNE1VcwA",
    "ln5drpv_ImI",
    "37ZuGYD3JOk",
    "C-opW0i5McE",
    "Bkci_8qcdvQ",
    "QtIXL7C4bB0",
    "UdvXJ95Yqt8",
    "J2bNC9gW5NI",
    "12IHVEFRacQ",
    "tLNRTxieD7k",
    "CR0E20Of7yU",
    "kDsNr-vu7ms",
    "RpDA3uYkJWM",
  ],
} as const;

/** The third screen: three rows of this hour's Explore wallpapers drifting by, each a way in. */
export const exploreRows = {
  title: "Explore",
} as const;

export const features: Feature[] = [
  {
    title: "The same on every device",
    visual: "devices",
    body: "Your computer, your Mac and your phone show the same photo. Change it on one, and the others follow within seconds.",
  },
  {
    title: "One schedule",
    visual: "schedule",
    body: "Set the time once and every device keeps it. Whichever wakes first picks the photo, so they never disagree.",
  },
  {
    title: "Fresh every morning",
    visual: "morning",
    body: "A new high-resolution photo at the time you choose, or as soon as you sign in.",
  },
  {
    title: "Your topics",
    visual: "topics",
    body: "Nature, space, cities, or your own searches. Skip anything you'd rather not see.",
  },
  {
    title: "Never repeats",
    visual: "history",
    body: "Every photo is remembered, so you won't see the same one twice.",
  },
  {
    title: "Every screen",
    visual: "screens",
    body: "All your monitors change together, and on your phone the home and lock screens too.",
  },
  {
    title: "Light and quiet",
    visual: "quiet",
    body: "Lives in the tray or the menu bar and sleeps between changes. Gentle on your phone's battery.",
  },
  {
    title: "In your browser too",
    visual: "browser",
    body: "A free new tab extension for Chrome, Edge, Brave and Firefox shows the same photo as your desktop, or its own picks.",
  },
  {
    title: "Favorites everywhere",
    visual: "favorites",
    body: "Star a photo on one device and it's a favorite on all of them. Each one links to its photographer on Unsplash.",
  },
];

export const steps: Step[] = [
  {
    title: "Download",
    body: "Get Oriel for Windows, Mac, Linux or Android. Sign in with Google or your email, no key needed.",
  },
  {
    title: "Install",
    body: "Run Oriel-Setup.exe on Windows, open Oriel.dmg on a Mac, Oriel.AppImage on Linux or Oriel.apk on Android. It settles in within seconds.",
  },
  {
    title: "Enjoy",
    body: "Your first wallpaper arrives right away. A new one every day after that.",
  },
];

export const faq: FaqItem[] = [
  {
    question: "Is Oriel free?",
    answer:
      "Yes. Oriel is free to download and use, with no ads. You just sign in with Google or your email.",
  },
  {
    question: "Do I need an Unsplash account or API key?",
    answer: "No. Oriel comes ready to use. Just download it and open it.",
  },
  {
    question: "Will it slow down my PC?",
    answer:
      "No. Oriel sleeps in the tray and only wakes up when it's time for a new photo. On Windows it runs in efficiency mode; on Linux it draws in software and hands its memory back when you close the window. A change takes a few seconds.",
  },
  {
    question: "Which versions of Windows does it support?",
    answer: "Windows 10 (version 2004 or later) and Windows 11, 64-bit.",
  },
  {
    question: "Which Linux desktops does it support?",
    answer:
      "GNOME, KDE Plasma, Xfce, Cinnamon, MATE, Budgie, LXQt, Sway and Hyprland, plus any X11 window manager with feh, nitrogen or xwallpaper installed. It runs as an AppImage on 64-bit distributions, with no root needed.",
  },
  {
    question: "Does the keyboard shortcut work on Linux?",
    answer:
      'On X11, yes: Ctrl+Alt+W by default. Wayland doesn\'t let apps listen for keys, so there you add a shortcut in your system keyboard settings that runs "oriel --change-now".',
  },
  {
    question: "How do I get updates?",
    answer:
      "Oriel updates itself. It checks once a day, downloads new versions in the background and installs them the next time it restarts. You can also check in Settings.",
  },
  {
    question: "Why does Windows warn me when I open the installer?",
    answer:
      "Oriel is new and in beta, so Windows SmartScreen doesn't know it yet. Click More info, then Run anyway. The warning goes away as more people install it. The Linux AppImage has no such warning.",
  },
  {
    question: "Is there a browser extension?",
    answer:
      "Yes. Oriel for browsers is a free new tab extension for Chrome, Edge, Brave, Opera, Vivaldi and Firefox. It shows an Unsplash photo with a clock and a quote on every new tab, and can match your desktop wallpaper when you sign in with the same account.",
  },
  {
    question: "Does Oriel work on macOS, Android or iOS?",
    answer:
      "On macOS 26 Tahoe and Android 8.0 or later, yes. Sign in with the same account and every device shows the same photo. There is no iPhone app yet.",
  },
  {
    question: "What data does Oriel collect?",
    answer:
      "Only what signing in needs: your Google name, email and picture, and the wallpaper your apps last set so your devices can match. No analytics, no ads, no tracking. See the privacy page for details.",
  },
  {
    question: "Where are the photos saved?",
    answer:
      "In your Pictures folder, under Oriel, by default. You can pick another folder in Settings, and Oriel moves your photos there.",
  },
  {
    question: "What is Explore?",
    answer:
      "A few hundred Unsplash wallpapers for computers and phones, picked once an hour and the same for everyone. Browse them in the apps or on the website's Explore page, and set any of them as your wallpaper in one click.",
  },
  {
    question: "Where do the photos come from?",
    answer:
      "From Unsplash, a community of photographers. Oriel credits each photographer and links to their profile.",
  },
];
