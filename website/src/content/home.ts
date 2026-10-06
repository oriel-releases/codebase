/** Copy for the home page. UI components only render what's here. */

export type Feature = { title: string; body: string };
export type Step = { title: string; body: string };
export type FaqItem = { question: string; answer: string };

export const hero = {
  eyebrow: "Free for Windows and Linux",
  title: "A new wallpaper, every day.",
  body: "Oriel quietly sets a beautiful Unsplash photo as your desktop each morning. Pick the topics you like, and forget about it.",
} as const;

export const features: Feature[] = [
  { title: "Fresh every morning", body: "A new high-resolution photo at the time you choose, or as soon as you sign in." },
  { title: "Your topics", body: "Nature, space, cities, or your own searches. Skip anything you'd rather not see." },
  { title: "Never repeats", body: "Every photo is remembered, so you won't see the same one twice." },
  { title: "Every screen", body: "All your monitors change together, on every virtual desktop that Windows or your Linux desktop allows." },
  { title: "Light and quiet", body: "Lives in the tray and sleeps between changes. Closing the window gives the memory back." },
  { title: "Credit where due", body: "Each photo links to its photographer on Unsplash. Keep your favorites forever." },
];

export const steps: Step[] = [
  { title: "Download", body: "Get Oriel for Windows or Linux. Sign in with Google, no key needed." },
  { title: "Install", body: "Run Oriel-Setup.exe on Windows, or open Oriel.AppImage on Linux. It settles into your tray in seconds." },
  { title: "Enjoy", body: "Your first wallpaper arrives right away. A new one every day after that." },
];

export const faq: FaqItem[] = [
  {
    question: "Is Oriel free?",
    answer: "Yes. Oriel is free to download and use, with no ads. You just sign in with Google.",
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
      "On X11, yes: Ctrl+Alt+W by default. Wayland doesn't let apps listen for keys, so there you add a shortcut in your system keyboard settings that runs \"oriel --change-now\".",
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
    question: "Where are the photos saved?",
    answer: "In your Pictures folder, under Oriel, by default. You can pick another folder in Settings, and Oriel moves your photos there.",
  },
  {
    question: "Where do the photos come from?",
    answer: "From Unsplash, a community of photographers. Oriel credits each photographer and links to their profile.",
  },
];
