import type { Metadata } from "next";
import { routes } from "@/config/site";
import { pageMetadata } from "@/lib/seo/metadata";
import { SharedWallpaper } from "@/components/share/SharedWallpaper";

export const metadata: Metadata = {
  ...pageMetadata({
    title: "A wallpaper was shared with you",
    description:
      "A friend shared a wallpaper with you. Set it on your computer in one click with Oriel, a free daily wallpaper app for Windows and Linux.",
    path: routes.share,
    image: { url: "/share/opengraph-image", alt: "A wallpaper was shared with you" },
  }),
  // Every share link is its own page, so none of them belong in search results. robots.txt must NOT block this path:
  // crawlers and link previews have to be able to fetch the page to see this tag.
  robots: { index: false, follow: true },
};

/** One screen with its footer and no scrolling: who sent it, the photo, one button. */
export default function SharePage() {
  return <SharedWallpaper />;
}
