import type { Metadata } from "next";
import { routes } from "@/config/site";
import { pageMetadata } from "@/lib/seo/metadata";
import { FollowInvite } from "@/components/share/FollowInvite";

export const metadata: Metadata = {
  ...pageMetadata({
    title: "Follow a friend's wallpaper",
    description:
      "A friend shares their wallpaper with you. Follow it with Oriel, a free wallpaper app for Windows and Linux: whenever they change theirs, yours changes too.",
    path: routes.follow,
    image: { url: "/share/opengraph-image", alt: "A wallpaper is shared with you" },
  }),
  // Every follow link is its own page, so none belong in search results (crawlers still read this tag).
  robots: { index: false, follow: true },
};

/** One screen: whose wallpaper, what it looks like, one button. */
export default function FollowPage() {
  return <FollowInvite />;
}
