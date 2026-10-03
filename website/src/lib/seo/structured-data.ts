import type { Release } from "@/config/release";
import { absoluteUrl, routes, site } from "@/config/site";
import type { Feature, FaqItem } from "@/content/home";

/**
 * schema.org JSON-LD builders. Structured data helps search engines and AI answer engines
 * describe Oriel accurately (what it is, what it costs, where to get it).
 */

type JsonLd = Record<string, unknown>;

export function websiteSchema(): JsonLd {
  return {
    "@context": "https://schema.org",
    "@type": "WebSite",
    name: site.name,
    url: absoluteUrl(routes.home),
    description: site.description,
  };
}

export function softwareApplicationSchema(release: Release, features: Feature[]): JsonLd {
  return {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    name: site.name,
    description: site.description,
    url: absoluteUrl(routes.home),
    image: absoluteUrl("/logo.png"),
    applicationCategory: "DesktopEnhancementApplication",
    operatingSystem: "Windows 10, Windows 11",
    softwareVersion: release.version,
    datePublished: release.releasedOn,
    downloadUrl: absoluteUrl(release.url),
    softwareRequirements: release.requirements,
    featureList: features.map((f) => f.title),
    offers: { "@type": "Offer", price: "0", priceCurrency: "USD" },
  };
}

export function faqSchema(items: FaqItem[]): JsonLd {
  return {
    "@context": "https://schema.org",
    "@type": "FAQPage",
    mainEntity: items.map((item) => ({
      "@type": "Question",
      name: item.question,
      acceptedAnswer: { "@type": "Answer", text: item.answer },
    })),
  };
}

export function breadcrumbSchema(trail: { name: string; path: string }[]): JsonLd {
  return {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: trail.map((crumb, i) => ({
      "@type": "ListItem",
      position: i + 1,
      name: crumb.name,
      item: absoluteUrl(crumb.path),
    })),
  };
}

/** JSON for a <script> tag, with "<" escaped so content can't close the tag (XSS). */
export function serializeJsonLd(data: JsonLd | JsonLd[]): string {
  return JSON.stringify(data).replace(/</g, "\\u003c");
}
