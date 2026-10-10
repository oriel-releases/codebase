/** Copy for the extension page. */
import type { FaqItem, Feature, Step } from "@/content/home";

export const extensionPage = {
  eyebrow: "Free for Chrome, Edge, Brave and Firefox",
  title: "A beautiful photo on every new tab.",
  body: "Oriel for browsers turns the new tab page into a calm, full-screen Unsplash photo with a clock and a quote. Let it match your desktop wallpaper, or pick your own topics.",
  metaTitle: "New tab extension for Chrome, Edge and Firefox",
  metaDescription:
    "Oriel is a free new tab extension for Chrome, Edge, Brave and Firefox: a fresh Unsplash photo, a clock and a quote on every tab. Match your Oriel desktop wallpaper or pick your own topics. No ads, no tracking.",
} as const;

export const extensionFeatures: Feature[] = [
  { title: "Match my desktop", body: "Sign in with your Oriel account and every new tab shows the wallpaper Oriel for Windows or Linux set last, changing the moment it does." },
  { title: "Or pick your own", body: "Choose topics like nature, space or architecture and how often the photo changes: every tab, hourly or daily. Works right away, signed out, with no account." },
  { title: "Clock, quote, search", body: "A clock and a short quote sit over the photo, with an optional search bar that uses your browser's own search engine. Turn any of them off." },
  { title: "Light and private", body: "No background page, no analytics, no ads. Nothing runs when no new tab is open, and Oriel never reads your browsing history." },
];

export const extensionSteps: Step[] = [
  { title: "Add it", body: "Install Oriel from your browser's store. Your new tab shows a photo straight away." },
  { title: "Choose", body: "Open the gear on the new tab to pick topics, or connect your Oriel account to follow your desktop." },
  { title: "Enjoy", body: "A new photo every tab, every hour or every day, credited to its photographer." },
];

export const extensionFaq: FaqItem[] = [
  {
    question: "Is the Oriel extension free?",
    answer: "Yes. It is free, has no ads and needs no account. Signing in is optional and only used to follow your desktop wallpaper.",
  },
  {
    question: "Which browsers does it work in?",
    answer: "Google Chrome, Microsoft Edge, Brave, Opera, Vivaldi and Mozilla Firefox, from the same code. Chromium browsers install it from the Chrome Web Store or Edge Add-ons; Firefox from Firefox Add-ons.",
  },
  {
    question: "Do I need the Oriel desktop app?",
    answer: "No. On its own the extension picks photos from a built-in set. Install Oriel for Windows or Linux and sign in with the same account to make your new tab match your desktop wallpaper.",
  },
  {
    question: "What data does the extension collect?",
    answer: "None unless you sign in. If you do, it uses your Google name and email to link your account, and reads the one document that holds your current wallpaper. It never reads your browsing history, the pages you open or what you type. Details are on the privacy page.",
  },
  {
    question: "Does it slow down my browser?",
    answer: "No. It has no background page and does nothing when no new tab is open. The photo is one image that your browser loads and caches.",
  },
  {
    question: "Where do the photos come from?",
    answer: "From Unsplash, a community of photographers. Each photo links to its photographer.",
  },
];
