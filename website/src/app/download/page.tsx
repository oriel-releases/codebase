import { releases } from "@/config/release";
import { routes, site } from "@/config/site";
import { downloadPage } from "@/content/download";
import { features } from "@/content/home";
import { pageMetadata } from "@/lib/seo/metadata";
import {
  breadcrumbSchema,
  softwareApplicationSchema,
} from "@/lib/seo/structured-data";
import { DownloadOptions } from "@/components/download/DownloadOptions";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { JsonLd } from "@/components/seo/JsonLd";
import { Container } from "@/components/ui/Container";
import { Logo } from "@/components/ui/Logo";
import { SupportHint } from "@/components/ui/SupportCard";
import { PageTransition } from "@/components/motion/PageTransition";
import { SharedElement } from "@/components/motion/SharedElement";
import { sharedElement } from "@/lib/transitions";

export const metadata = pageMetadata({
  title: "Download for Windows, Mac, Linux and Android",
  description: `Download ${site.name} for Windows (${releases.windows.version}), Mac (${releases.macos.version}), Linux (${releases.linux.version}) and Android (${releases.android.version}). Free, with no ads: sign in with Google or your email and your first wallpaper arrives right away.`,
  path: routes.download,
});

/**
 * Every way to get Oriel on one screen: the apps (the visitor's own system first), then the browser extension, with a
 * jump between the two at the top.
 */
export default function DownloadPage() {
  return (
    <div className="flex min-h-svh flex-col">
      <JsonLd
        data={[
          softwareApplicationSchema(releases, features),
          breadcrumbSchema([
            { name: site.name, path: routes.home },
            { name: "Download", path: routes.download },
          ]),
        ]}
      />
      <PageTransition>
        <div className="flex-1 pt-28 pb-20 sm:pt-32">
          <Container className="flex flex-col items-center text-center">
            <SharedElement name={sharedElement.logo}>
              <Logo size={64} priority className="size-12 sm:size-16" />
            </SharedElement>
            <h1 className="mt-7 text-4xl font-semibold tracking-tight text-balance sm:text-6xl">
              {downloadPage.title}
            </h1>
            <p className="mt-4 max-w-xl text-pretty text-muted sm:text-lg"></p>

            <div className="mt-10 w-full">
              <DownloadOptions releases={releases} />
            </div>

            <div className="mt-14">
              <SupportHint>Trouble installing?</SupportHint>
            </div>
          </Container>
        </div>
      </PageTransition>
      <SiteFooter />
    </div>
  );
}
