import type { Metadata } from "next";
import { site } from "@/config/site";

type PageSeo = {
  title: string;
  description: string;
  path: string;
};

/** Per-page metadata: canonical URL plus matching Open Graph and Twitter fields. */
export function pageMetadata({ title, description, path }: PageSeo): Metadata {
  return {
    title,
    description,
    alternates: { canonical: path },
    openGraph: {
      type: "website",
      siteName: site.name,
      locale: site.locale,
      url: path,
      title,
      description,
    },
    twitter: {
      card: "summary_large_image",
      title,
      description,
    },
  };
}
