import { routes, site } from "@/config/site";
import { pageMetadata } from "@/lib/seo/metadata";
import { LoginCard } from "@/components/login/LoginCard";
import { SiteFooter } from "@/components/layout/SiteFooter";
import { Container } from "@/components/ui/Container";
import { Logo } from "@/components/ui/Logo";
import { PageTransition } from "@/components/motion/PageTransition";

export const metadata = {
  ...pageMetadata({
    title: "Sign in",
    description: `Sign in to ${site.name} with Google.`,
    path: routes.login,
  }),
  robots: { index: false, follow: false },
};

export default function LoginPage() {
  return (
    <div className="flex h-svh flex-col overflow-hidden">
      <PageTransition>
        <section className="flex min-h-0 flex-1 items-center pt-16">
          <Container className="flex max-w-sm flex-col items-center text-center">
            <Logo size={56} priority className="size-12 sm:size-14" />
            <h1 className="mt-8 mb-8 text-3xl font-semibold tracking-tight">Sign in to Oriel</h1>
            <LoginCard />
          </Container>
        </section>
      </PageTransition>
      <SiteFooter />
    </div>
  );
}
