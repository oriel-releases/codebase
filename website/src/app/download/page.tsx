import { releases } from "@/config/release";
import { routes, site } from "@/config/site";
import { downloadPage } from "@/content/download";
import { features } from "@/content/home";
import { pageMetadata } from "@/lib/seo/metadata";
import { breadcrumbSchema, softwareApplicationSchema } from "@/lib/seo/structured-data";
import { DesktopPreview } from "@/components/download/DesktopPreview";
import { DownloadPicker } from "@/components/download/DownloadPicker";
import { ExtensionPicker } from "@/components/download/ExtensionPicker";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { JsonLd } from "@/components/seo/JsonLd";
import { Container } from "@/components/ui/Container";
import { Logo } from "@/components/ui/Logo";
import { SupportHint } from "@/components/ui/SupportCard";
import { PageTransition } from "@/components/motion/PageTransition";
import { SharedElement } from "@/components/motion/SharedElement";
import { sharedElement } from "@/lib/transitions";

export const metadata = pageMetadata({
  title: "Download for Windows, Linux and Android",
  description: `Download ${site.name} for Windows (${releases.windows.version}), Linux (${releases.linux.version}) and Android (${releases.android.version}). Free, with no ads: sign in with Google or your email and your first wallpaper arrives right away.`,
  path: routes.download,
});

/**
 * Top to bottom, in the order a visitor needs it: the one download for their system, a look at what they get, how to
 * install it, then the browser extension. Everything is as wide as the header, like the rest of the site.
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
            <h1 className="mt-7 text-4xl font-semibold tracking-tight text-balance sm:text-6xl">{downloadPage.title}</h1>

            <div className="mt-10 w-full">
              <DownloadPicker releases={releases} />
            </div>
          </Container>

          {/* What you get: a little wider than the text, so it reads as the page's picture. */}
          <Container className="mt-16 sm:mt-20">
            <DesktopPreview />
          </Container>

          <Container className="mt-20 flex flex-col items-center text-center sm:mt-28">
            <h2 className="text-2xl font-semibold tracking-tight sm:text-3xl">{downloadPage.extension.title}</h2>
            <div className="mt-8 w-full">
              <ExtensionPicker />
            </div>
            <div className="mt-12">
              <SupportHint>Trouble installing?</SupportHint>
            </div>
          </Container>
        </div>
      </PageTransition>
      <SiteFooter />
    </div>
  );
}
