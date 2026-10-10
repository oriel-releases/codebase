import type { Metadata } from "next";
import { site } from "@/config/site";

type PageSeo = {
  title: string;
  description: string;
  path: string;
  /** Skip the " · Oriel" title suffix (the home page, whose title already carries the name). */
  absoluteTitle?: boolean;
  /** A page's own social preview (its opengraph-image route), instead of the site's. */
  image?: { url: string; alt: string };
};

/** The social preview image (app/opengraph-image.tsx). Set explicitly: a page's own openGraph/twitter objects would otherwise drop it. */
const preview = { url: "/opengraph-image", width: 1200, height: 630, alt: `${site.name}: ${site.tagline}` };

/** Per-page metadata: canonical URL plus matching Open Graph and Twitter fields. */
export function pageMetadata({ title, description, path, absoluteTitle = false, image }: PageSeo): Metadata {
  const picture = image ? { ...image, width: 1200, height: 630 } : preview;
  return {
    title: absoluteTitle ? { absolute: title } : title,
    description,
    alternates: { canonical: path },
    openGraph: {
      type: "website",
      siteName: site.name,
      locale: site.locale,
      url: path,
      title,
      description,
      images: [picture],
    },
    twitter: {
      card: "summary_large_image",
      title,
      description,
      images: [picture.url],
    },
  };
}
