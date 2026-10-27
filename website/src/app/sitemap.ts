import type { MetadataRoute } from "next";
import { lastReleasedOn } from "@/config/release";
import { absoluteUrl, routes } from "@/config/site";
import { privacyPage } from "@/content/privacy";

export const dynamic = "force-static";

export default function sitemap(): MetadataRoute.Sitemap {
  const lastModified = new Date(lastReleasedOn);
  return [
    { url: absoluteUrl(routes.home), lastModified, changeFrequency: "monthly", priority: 1 },
    { url: absoluteUrl(routes.download), lastModified, changeFrequency: "monthly", priority: 0.9 },
    { url: absoluteUrl(routes.extension), lastModified, changeFrequency: "monthly", priority: 0.8 },
    // New wallpapers every hour.
    { url: absoluteUrl(routes.explore), changeFrequency: "hourly", priority: 0.8 },
    // A new photo every day.
    { url: absoluteUrl(routes.today), changeFrequency: "daily", priority: 0.7 },
    { url: absoluteUrl(routes.privacy), lastModified: new Date(privacyPage.updatedOn), changeFrequency: "yearly", priority: 0.2 },
  ];
}
