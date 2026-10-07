import { routes, site } from "@/config/site";
import { privacyPage } from "@/content/privacy";
import { pageMetadata } from "@/lib/seo/metadata";
import { breadcrumbSchema } from "@/lib/seo/structured-data";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { JsonLd } from "@/components/seo/JsonLd";
import { Container } from "@/components/ui/Container";
import { SupportHint } from "@/components/ui/SupportCard";
import { PageTransition } from "@/components/motion/PageTransition";

export const metadata = pageMetadata({
  title: privacyPage.title,
  description: `What ${site.name} stores, where, and how to remove it. No analytics, no ads, no selling of data.`,
  path: routes.privacy,
});

export default function PrivacyPage() {
  return (
    <div className="flex min-h-svh flex-col">
      <JsonLd
        data={breadcrumbSchema([
          { name: site.name, path: routes.home },
          { name: privacyPage.title, path: routes.privacy },
        ])}
      />
      <PageTransition>
        <article className="flex-1 py-28">
          <Container className="max-w-2xl">
            <h1 className="text-3xl font-semibold tracking-tight sm:text-5xl">{privacyPage.title}</h1>
            <p className="mt-3 text-sm text-muted">Last updated {privacyPage.updatedOn}</p>
            <p className="mt-6 text-lg text-muted">{privacyPage.intro}</p>
            {privacyPage.sections.map((section) => (
              <section key={section.title} className="mt-10">
                <h2 className="text-xl font-semibold tracking-tight">{section.title}</h2>
                {section.body.map((paragraph) => (
                  <p key={paragraph} className="mt-3 text-muted">
                    {paragraph}
                  </p>
                ))}
              </section>
            ))}
            <div className="mt-12">
              <SupportHint>Questions about your data?</SupportHint>
            </div>
          </Container>
        </article>
      </PageTransition>
      <SiteFooter />
    </div>
  );
}
