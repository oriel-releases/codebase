import { routes, site } from "@/config/site";
import { pageMetadata } from "@/lib/seo/metadata";
import { LoginCard } from "@/components/login/LoginCard";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { Container } from "@/components/ui/Container";
import { Logo } from "@/components/ui/Logo";
import { PageTransition } from "@/components/motion/PageTransition";
import { SharedElement } from "@/components/motion/SharedElement";
import { sharedElement } from "@/lib/transitions";

export const metadata = {
  ...pageMetadata({
    title: "Sign in",
    description: `Sign in to ${site.name} with Google or your email.`,
    path: routes.login,
  }),
  robots: { index: false, follow: false },
};

export default function LoginPage() {
  return (
    <div className="flex min-h-svh flex-col">
      <PageTransition>
        <section className="flex flex-1 items-center pt-24 pb-12">
          <Container className="flex max-w-sm flex-col items-center text-center">
            <SharedElement name={sharedElement.logo}>
              <Logo size={56} priority className="size-12 sm:size-14" />
            </SharedElement>
            <h1 className="mt-8 mb-8 text-3xl font-semibold tracking-tight">Sign in to Oriel</h1>
            <LoginCard />
          </Container>
        </section>
      </PageTransition>
      <SiteFooter />
    </div>
  );
}
