/** Public URL in production builds; override with the SITE_URL environment variable. */
const productionUrl = "https://oriel.akbarjon.uz";

/** Site-wide facts. */
export const site = {
  name: "Oriel",
  tagline: "A new wallpaper, every day.",
  /** Release stage shown next to the name; set to null once Oriel leaves beta. */
  stage: "Beta" as string | null,
  description:
    "Oriel is a free daily wallpaper changer for Windows and Linux, with a matching new tab extension for Chrome, Edge, Brave and Firefox. Fresh Unsplash photos, lightweight, lives in your tray.",
  url: process.env.SITE_URL ?? (process.env.NODE_ENV === "production" ? productionUrl : "http://localhost:3000"),
  locale: "en_US",
  keywords: [
    "daily wallpaper",
    "Windows wallpaper app",
    "Linux wallpaper app",
    "Unsplash wallpaper",
    "wallpaper changer",
    "Windows 11 wallpaper",
    "GNOME KDE wallpaper changer",
    "desktop background",
    "new tab extension",
    "Chrome new tab wallpaper",
    "Firefox new tab photo",
    "Unsplash new tab",
  ],
  /** Telegram bot where people get help. */
  supportUrl: "https://t.me/oriel_support_bot",
  unsplashUrl: "https://unsplash.com/?utm_source=oriel&utm_medium=referral",
} as const;

export const routes = {
  home: "/",
  download: "/download",
  extension: "/extension",
  share: "/share",
  today: "/today",
  privacy: "/privacy",
  login: "/login",
  me: "/me",
} as const;

export function absoluteUrl(path: string): string {
  return new URL(path, site.url).toString();
}
