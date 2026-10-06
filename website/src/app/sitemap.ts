import type { MetadataRoute } from "next";
import { lastReleasedOn } from "@/config/release";
import { absoluteUrl, routes } from "@/config/site";

export const dynamic = "force-static";

export default function sitemap(): MetadataRoute.Sitemap {
  const lastModified = new Date(lastReleasedOn);
  return [
    { url: absoluteUrl(routes.home), lastModified, changeFrequency: "monthly", priority: 1 },
    { url: absoluteUrl(routes.download), lastModified, changeFrequency: "monthly", priority: 0.9 },
  ];
}
