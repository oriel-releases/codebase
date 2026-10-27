import { Suspense } from "react";
import { routes, site } from "@/config/site";
import { pageMetadata } from "@/lib/seo/metadata";
import { breadcrumbSchema, exploreSchema } from "@/lib/seo/structured-data";
import { ExploreGridSkeleton } from "@/components/explore/ExploreGrid";
import { ExploreView } from "@/components/explore/ExploreView";
import { ViewOnlyToggle } from "@/components/explore/ViewOnlyToggle";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { JsonLd } from "@/components/seo/JsonLd";
import { PageTransition } from "@/components/motion/PageTransition";

const description = `Hundreds of free Unsplash wallpapers for computers and phones, new every hour and the same for everyone. Set any of them in one click with ${site.name}, on Windows, Mac, Linux or Android.`;

export const metadata = pageMetadata({
  title: "Explore wallpapers",
  description,
  path: routes.explore,
  image: { url: "/explore/opengraph-image", alt: "Explore: new free wallpapers every hour" },
});

/**
 * The feed and the wallpaper in the address are only known in the browser: the static page carries the heading (for
 * search engines and screen readers) with the grid's shapes, and the rest arrives there.
 */
export default function ExplorePage() {
  return (
    <div className="flex min-h-svh flex-col">
      <JsonLd
        data={[
          exploreSchema(description),
          breadcrumbSchema([
            { name: site.name, path: routes.home },
            { name: "Explore", path: routes.explore },
          ]),
        ]}
      />
      <PageTransition>
        <div className="flex-1">
          <Suspense fallback={<ExploreGridSkeleton />}>
            <ExploreView />
          </Suspense>
        </div>
      </PageTransition>
      <SiteFooter extra={<ViewOnlyToggle />} />
    </div>
  );
}
