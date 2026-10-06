import { routes, site } from "@/config/site";
import { pageMetadata } from "@/lib/seo/metadata";
import { MeView } from "@/components/me/MeView";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { Container } from "@/components/ui/Container";
import { PageTransition } from "@/components/motion/PageTransition";

export const metadata = {
  ...pageMetadata({
    title: "Your account",
    description: `Your ${site.name} account: profile, current wallpaper and where you're signed in.`,
    path: routes.me,
  }),
  // Personal page: signed-out visitors are sent to /login anyway.
  robots: { index: false, follow: false },
};

export default function MePage() {
  return (
    <div className="flex min-h-svh flex-col">
      <PageTransition>
        <section className="flex-1 pt-28 pb-16">
          <Container className="max-w-3xl">
            <MeView />
          </Container>
        </section>
      </PageTransition>
      <SiteFooter />
    </div>
  );
}
