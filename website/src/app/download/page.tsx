import { release } from "@/config/release";
import { routes, site } from "@/config/site";
import { downloadPage } from "@/content/download";
import { features } from "@/content/home";
import { pageMetadata } from "@/lib/seo/metadata";
import { breadcrumbSchema, softwareApplicationSchema } from "@/lib/seo/structured-data";
import { DownloadCard } from "@/components/download/DownloadCard";
import { InstallSteps } from "@/components/download/InstallSteps";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { JsonLd } from "@/components/seo/JsonLd";
import { Container } from "@/components/ui/Container";
import { Logo } from "@/components/ui/Logo";
import { PageTransition } from "@/components/motion/PageTransition";
import { SharedElement } from "@/components/motion/SharedElement";
import { sharedElement } from "@/lib/transitions";

export const metadata = pageMetadata({
  title: "Download for Windows",
  description: `Download ${site.name} ${release.version} for Windows. ${downloadPage.body}`,
  path: routes.download,
});

/**
 * Exactly one screen tall, never scrolls. On short screens the less important parts
 * (install steps, then the logo) step aside so the download button always fits.
 */
export default function DownloadPage() {
  return (
    <div className="flex h-svh flex-col overflow-hidden">
      <JsonLd
        data={[
          softwareApplicationSchema(release, features),
          breadcrumbSchema([
            { name: site.name, path: routes.home },
            { name: "Download", path: routes.download },
          ]),
        ]}
      />
      <PageTransition>
        <section className="flex min-h-0 flex-1 items-center pt-16">
          <Container className="flex max-w-xl flex-col items-center text-center">
            <SharedElement name={sharedElement.logo}>
              <Logo size={72} priority className="size-14 sm:size-18 [@media(max-height:560px)]:hidden" />
            </SharedElement>
            <h1 className="mt-8 text-3xl font-semibold tracking-tight sm:mt-10 sm:text-5xl [@media(max-height:560px)]:mt-0">
              {downloadPage.title}
            </h1>
            <p className="mt-4 text-base text-pretty text-muted sm:mt-5 sm:text-lg">{downloadPage.body}</p>
            <div className="mt-8 w-full sm:mt-10">
              <DownloadCard release={release} />
            </div>
            <div className="mt-10 w-full sm:mt-14 [@media(max-height:760px)]:hidden">
              <InstallSteps steps={downloadPage.installSteps} />
            </div>
          </Container>
        </section>
      </PageTransition>
      <SiteFooter />
    </div>
  );
}
