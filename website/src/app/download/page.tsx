import { releases } from "@/config/release";
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
import { SupportHint } from "@/components/ui/SupportCard";
import { PageTransition } from "@/components/motion/PageTransition";
import { SharedElement } from "@/components/motion/SharedElement";
import { sharedElement } from "@/lib/transitions";

export const metadata = pageMetadata({
  title: "Download for Windows and Linux",
  description: `Download ${site.name} for Windows (${releases.windows.version}) and Linux (${releases.linux.version}). Free, with no ads: sign in with Google and your first wallpaper arrives right away.`,
  path: routes.download,
});

/** At least one screen tall; on small screens the cards and steps simply scroll. */
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
        <section className="flex flex-1 items-center py-28">
          <Container className="flex max-w-3xl flex-col items-center text-center">
            <SharedElement name={sharedElement.logo}>
              <Logo size={72} priority className="size-14 sm:size-18" />
            </SharedElement>
            <h1 className="mt-8 text-3xl font-semibold tracking-tight sm:mt-10 sm:text-5xl">
              {downloadPage.title}
            </h1>
            <div className="mt-8 w-full sm:mt-10">
              <DownloadCard releases={releases} />
            </div>
            <div className="mt-8 w-full border-t border-line pt-6 sm:mt-10 sm:pt-8">
              <InstallSteps steps={downloadPage.installSteps} />
            </div>
            <div className="mt-6">
              <SupportHint>Trouble installing?</SupportHint>
            </div>
          </Container>
        </section>
      </PageTransition>
      <SiteFooter />
    </div>
  );
}
