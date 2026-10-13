import { lastReleasedOn, type Releases } from "@/config/release";
import { absoluteUrl, routes, site } from "@/config/site";
import { extension } from "@/config/extension";
import type { Feature, FaqItem } from "@/content/home";

/**
 * schema.org JSON-LD builders. Structured data helps search engines and AI answer engines
 * describe Oriel accurately (what it is, what it costs, where to get it).
 */

type JsonLd = Record<string, unknown>;

const organizationId = `${absoluteUrl(routes.home)}#organization`;

/** Who publishes Oriel; referenced by id from the other schemas so engines see one entity. */
export function organizationSchema(): JsonLd {
  return {
    "@context": "https://schema.org",
    "@type": "Organization",
    "@id": organizationId,
    name: site.name,
    url: absoluteUrl(routes.home),
    logo: absoluteUrl("/logo.png"),
    description: site.description,
    sameAs: [site.supportUrl, "https://github.com/oriel-releases"],
  };
}

export function websiteSchema(): JsonLd {
  return {
    "@context": "https://schema.org",
    "@type": "WebSite",
    name: site.name,
    url: absoluteUrl(routes.home),
    description: site.description,
    inLanguage: "en",
    publisher: { "@id": organizationId },
  };
}

export function softwareApplicationSchema(releases: Releases, features: Feature[]): JsonLd {
  return {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    name: site.name,
    description: site.description,
    url: absoluteUrl(routes.home),
    image: absoluteUrl("/logo.png"),
    applicationCategory: "DesktopEnhancementApplication",
    operatingSystem: "Windows 10, Windows 11, Linux, Android",
    softwareVersion: `${releases.windows.version} (Windows), ${releases.linux.version} (Linux), ${releases.android.version} (Android)`,
    datePublished: lastReleasedOn,
    downloadUrl: [releases.windows.url, releases.linux.url, releases.android.url],
    softwareRequirements: `${releases.windows.requirements}; ${releases.linux.requirements}; ${releases.android.requirements}`,
    featureList: features.map((f) => f.title),
    offers: { "@type": "Offer", price: "0", priceCurrency: "USD" },
    publisher: { "@id": organizationId },
  };
}

/** The new tab extension: a free browser application, installed from each browser's store. */
export function extensionSchema(features: Feature[]): JsonLd {
  const installs = extension.stores.flatMap((s) => (s.url ? [s.url] : []));
  return {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    name: extension.name,
    alternateName: "Oriel new tab extension",
    description: "A free new tab extension for Chrome, Edge, Brave and Firefox that shows an Unsplash photo, a clock and a quote, and can match your Oriel desktop wallpaper.",
    url: absoluteUrl(routes.extension),
    image: absoluteUrl("/logo.png"),
    applicationCategory: "BrowserApplication",
    operatingSystem: "Chrome, Microsoft Edge, Brave, Opera, Vivaldi, Firefox",
    softwareVersion: extension.version,
    featureList: features.map((f) => f.title),
    offers: { "@type": "Offer", price: "0", priceCurrency: "USD" },
    publisher: { "@id": organizationId },
    ...(installs.length > 0 && { installUrl: installs }),
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
