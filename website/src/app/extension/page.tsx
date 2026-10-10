import { releases } from "@/config/release";
import { routes, site } from "@/config/site";
import { extensionFaq, extensionFeatures, extensionPage, extensionSteps } from "@/content/extension";
import { pageMetadata } from "@/lib/seo/metadata";
import { breadcrumbSchema, extensionSchema, faqSchema } from "@/lib/seo/structured-data";
import { ExtensionPicker } from "@/components/download/ExtensionPicker";
import { Faq } from "@/components/home/Faq";
import { Features } from "@/components/home/Features";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { PageTransition } from "@/components/motion/PageTransition";
import { SharedElement } from "@/components/motion/SharedElement";
import { JsonLd } from "@/components/seo/JsonLd";
import { ButtonLink } from "@/components/ui/Button";
import { Container } from "@/components/ui/Container";
import { DownloadIcon } from "@/components/ui/icons";
import { Logo } from "@/components/ui/Logo";
import { Section } from "@/components/ui/Section";
import { SectionHeading } from "@/components/ui/SectionHeading";
import { navigation, sharedElement } from "@/lib/transitions";

export const metadata = pageMetadata({
  title: extensionPage.metaTitle,
  description: extensionPage.metaDescription,
  path: routes.extension,
});

export default function ExtensionPage() {
  return (
    <>
      <JsonLd
        data={[
          extensionSchema(extensionFeatures),
          faqSchema(extensionFaq),
          breadcrumbSchema([
            { name: site.name, path: routes.home },
            { name: "Extension", path: routes.extension },
          ]),
        ]}
      />
      <PageTransition>
        <div>
          <Section className="pt-28">
            <Container className="flex max-w-3xl flex-col items-center text-center">
              <SharedElement name={sharedElement.logo}>
                <Logo size={96} priority className="size-20 sm:size-24" />
              </SharedElement>
              <p className="mt-10 rounded-full bg-surface px-4 py-1.5 text-xs font-medium text-accent sm:mt-12">{extensionPage.eyebrow}</p>
              <h1 className="mt-6 max-w-2xl text-4xl font-semibold tracking-tight text-balance sm:text-6xl">{extensionPage.title}</h1>
              <p className="mt-6 max-w-xl text-base text-pretty text-muted sm:text-lg">{extensionPage.body}</p>
              <div className="mt-10 w-full sm:mt-12">
                <ExtensionPicker />
              </div>
              <p className="mt-6 text-sm text-muted">
                Want it to match your desktop?{" "}
                <a href={routes.download} className="underline underline-offset-4 transition-colors hover:text-ink">
                  Get Oriel {releases.windows.name} or {releases.linux.name}
                </a>
                .
              </p>
            </Container>
          </Section>
          <Features items={extensionFeatures} />
          <Section labelledBy="steps">
            <Container>
              <SectionHeading id="steps">Set up in a minute</SectionHeading>
              <ol className="mt-12 grid gap-4 md:grid-cols-3">
                {extensionSteps.map((s, i) => (
                  <li key={s.title} className="group rounded-[2rem] border border-line p-7 transition-[translate,box-shadow,border-color] duration-300 ease-out motion-reduce:transition-none  sm:p-8">
                    <span className="flex size-9 items-center justify-center rounded-full bg-accent text-sm font-medium text-paper transition-transform duration-300 ">
                      {i + 1}
                    </span>
                    <h3 className="mt-6 font-medium">{s.title}</h3>
                    <p className="mt-2 text-muted">{s.body}</p>
                  </li>
                ))}
              </ol>
              <div className="mt-12 flex justify-center">
                <ButtonLink href={routes.download} transitionTypes={[...navigation.forward]} variant="secondary">
                  <DownloadIcon width={16} height={16} />
                  Download the desktop app
                </ButtonLink>
              </div>
            </Container>
          </Section>
          <Faq items={extensionFaq} />
          <SiteFooter wordmark />
        </div>
      </PageTransition>
    </>
  );
}
