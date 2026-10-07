import { releases } from "@/config/release";
import { routes, site } from "@/config/site";
import { faq, features, steps } from "@/content/home";
import { pageMetadata } from "@/lib/seo/metadata";
import { faqSchema, organizationSchema, softwareApplicationSchema, websiteSchema } from "@/lib/seo/structured-data";
import { Faq } from "@/components/home/Faq";
import { Features } from "@/components/home/Features";
import { GetStarted } from "@/components/home/GetStarted";
import { Hero } from "@/components/home/Hero";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { JsonLd } from "@/components/seo/JsonLd";
import { PageTransition } from "@/components/motion/PageTransition";

export const metadata = pageMetadata({
  title: `${site.name}: Daily Wallpaper Changer for Windows and Linux`,
  description: site.description,
  path: routes.home,
  absoluteTitle: true,
});

export default function HomePage() {
  return (
    <>
      <JsonLd data={[organizationSchema(), websiteSchema(), softwareApplicationSchema(releases, features), faqSchema(faq)]} />
      <PageTransition>
        <div>
          <Hero />
          <Features items={features} />
          <GetStarted steps={steps} />
          <Faq items={faq} />
          <SiteFooter wordmark />
        </div>
      </PageTransition>
    </>
  );
}
