import { routes, site } from "@/config/site";
import { pageMetadata } from "@/lib/seo/metadata";
import { breadcrumbSchema } from "@/lib/seo/structured-data";
import { WallpaperOfTheDay } from "@/components/today/WallpaperOfTheDay";
import { JsonLd } from "@/components/seo/JsonLd";

export const metadata = pageMetadata({
  title: "Wallpaper of the day",
  description: `A new free Unsplash wallpaper every day, the same for everyone. Set it on your Windows or Linux desktop in one click with ${site.name}.`,
  path: routes.today,
  image: { url: "/today/opengraph-image", alt: "Wallpaper of the day" },
});

/**
 * Laid out like /share: one screen, the photo, one button. The heading and a sentence are in the static page for
 * search engines and screen readers (the photo is picked in the browser, so the visible label comes later).
 */
export default function TodayPage() {
  return (
    <>
      <JsonLd
        data={[
          breadcrumbSchema([
            { name: site.name, path: routes.home },
            { name: "Wallpaper of the day", path: routes.today },
          ]),
        ]}
      />
      <h1 className="sr-only">Wallpaper of the day</h1>
      <p className="sr-only">A new photo every day, the same for everyone, free from Unsplash. Set it as your wallpaper in one click with {site.name}.</p>
      <WallpaperOfTheDay />
    </>
  );
}
