/** Copy for the home page. UI components only render what's here. */

export type Feature = { title: string; body: string };
export type Step = { title: string; body: string };
export type FaqItem = { question: string; answer: string };

export const hero = {
  eyebrow: "Free for Windows",
  title: "A new wallpaper, every day.",
  body: "Oriel quietly sets a beautiful Unsplash photo as your desktop each morning. Pick the topics you like, and forget about it.",
} as const;

export const features: Feature[] = [
  { title: "Fresh every morning", body: "A new high-resolution photo at the time you choose, or as soon as you sign in." },
  { title: "Your topics", body: "Nature, space, cities, or your own searches. Skip anything you'd rather not see." },
  { title: "Never repeats", body: "Every photo is remembered, so you won't see the same one twice." },
  { title: "Every desktop", body: "All monitors and all virtual desktops change together, or one photo per screen." },
  { title: "Light and quiet", body: "Lives in the tray in efficiency mode. Closing the window gives the memory back." },
  { title: "Credit where due", body: "Each photo links to its photographer on Unsplash. Keep your favorites forever." },
];

export const steps: Step[] = [
  { title: "Download", body: "Get the installer for Windows. No account, no key, no sign-up." },
  { title: "Install", body: "Run Oriel-Setup.exe. It takes a few seconds and settles into your tray." },
  { title: "Enjoy", body: "Your first wallpaper arrives right away. A new one every day after that." },
];

export const faq: FaqItem[] = [
  {
    question: "Is Oriel free?",
    answer: "Yes. Oriel is free to download and use, with no ads and no account.",
  },
  {
    question: "Do I need an Unsplash account or API key?",
    answer: "No. Oriel comes ready to use. Just download it and open it.",
  },
  {
    question: "Will it slow down my PC?",
    answer:
      "No. Oriel sleeps in the tray in Windows efficiency mode and only wakes up when it's time for a new photo. A change takes a few seconds.",
  },
  {
    question: "Which versions of Windows does it support?",
    answer: "Windows 10 (version 2004 or later) and Windows 11, 64-bit.",
  },
  {
    question: "How do I get updates?",
    answer:
      "Oriel updates itself. It checks once a day, downloads new versions in the background and installs them the next time it restarts. You can also check in Settings.",
  },
  {
    question: "Why does Windows warn me when I open the installer?",
    answer:
      "Oriel is new and in beta, so Windows SmartScreen doesn't know it yet. Click More info, then Run anyway. The warning goes away as more people install it.",
  },
  {
    question: "Where are the photos saved?",
    answer: "In Pictures\\Oriel by default. You can pick another folder in Settings, and Oriel moves your photos there.",
  },
  {
    question: "Where do the photos come from?",
    answer: "From Unsplash, a community of photographers. Oriel credits each photographer and links to their profile.",
  },
];
