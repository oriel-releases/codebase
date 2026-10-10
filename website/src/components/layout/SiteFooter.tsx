import Link from "next/link";
import { routes, site } from "@/config/site";
import { Container } from "@/components/ui/Container";
import { TelegramIcon } from "@/components/ui/icons";
import { navigation } from "@/lib/transitions";

/** Copyright and links. With `wordmark`, ends the page with the name set huge and fading out. */
export function SiteFooter({ wordmark = false }: { wordmark?: boolean }) {
  return (
    <footer className="overflow-hidden text-sm text-muted">
      <Container className="flex flex-col items-center justify-between gap-3 py-6 sm:flex-row">
        <p className="text-center">
          © {new Date().getFullYear()} {site.name}. Photos by{" "}
          <a href={site.unsplashUrl} className="underline underline-offset-4 transition-colors hover:text-ink">
            Unsplash
          </a>
          .
        </p>
        <nav className="flex gap-6">
          <Link href={routes.home} transitionTypes={[...navigation.back]} className="grow transition-colors hover:text-ink">
            Home
          </Link>
          <Link href={routes.download} transitionTypes={[...navigation.forward]} className="grow transition-colors hover:text-ink">
            Download
          </Link>
          <Link href={routes.today} transitionTypes={[...navigation.forward]} className="grow transition-colors hover:text-ink">
            Today
          </Link>
          <Link href={routes.privacy} transitionTypes={[...navigation.forward]} className="grow transition-colors hover:text-ink">
            Privacy
          </Link>
          <a href={site.supportUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1.5 grow transition-colors hover:text-ink">
            <TelegramIcon width={14} height={14} />
            Support
          </a>
        </nav>
      </Container>
      {wordmark && (
        <p
          aria-hidden
          className="-mb-[0.18em] bg-linear-to-b from-ink/30 to-ink/0 bg-clip-text pt-8 text-center text-[30vw] leading-[0.8] font-semibold tracking-[-0.07em] text-transparent select-none pointer-events-none"
        >
          {site.name.toLowerCase()}
        </p>
      )}
    </footer>
  );
}
