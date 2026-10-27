import Link from "next/link";
import type { ReactNode } from "react";
import { routes, site } from "@/config/site";
import { Container } from "@/components/ui/Container";
import { TelegramIcon } from "@/components/ui/icons";
import { navigation } from "@/lib/transitions";

/**
 * Copyright and links. With `wordmark`, ends the page with the name set huge and fading out (nearly the full width on
 * a phone). Pages render it inside
 * their own layout (some fit it on one screen with the content), which puts it inside <main>; `contentinfo` keeps it
 * the page's footer for screen readers all the same.
 */
export function SiteFooter({ wordmark = false, extra }: { wordmark?: boolean; extra?: ReactNode }) {
  return (
    <footer role="contentinfo" className="relative overflow-hidden text-sm text-muted">
      <Container className="flex flex-col items-center justify-between gap-3 py-6 sm:flex-row">
        <p data-chrome="down" className="text-center">
          © {new Date().getFullYear()} {site.name}. Photos by{" "}
          <a href={site.unsplashUrl} className="underline underline-offset-4 transition-colors hover:text-ink">
            Unsplash
          </a>
          .
        </p>
        <nav data-chrome="down" aria-label="Footer" className="flex flex-wrap justify-center gap-x-6 gap-y-2">
          <Link href={routes.home} transitionTypes={[...navigation.back]} className="transition-colors hover:text-ink">
            Home
          </Link>
          <Link href={routes.download} transitionTypes={[...navigation.forward]} className="transition-colors hover:text-ink">
            Download
          </Link>
          <Link href={routes.explore} transitionTypes={[...navigation.forward]} className="transition-colors hover:text-ink">
            Explore
          </Link>
          <Link href={routes.today} transitionTypes={[...navigation.forward]} className="transition-colors hover:text-ink">
            Today
          </Link>
          <Link href={routes.privacy} transitionTypes={[...navigation.forward]} className="transition-colors hover:text-ink">
            Privacy
          </Link>
          <a href={site.supportUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1.5 transition-colors hover:text-ink">
            <TelegramIcon width={14} height={14} />
            Support
          </a>
        </nav>
      </Container>
      {/* A page's own control at the footer's far right, out of its flow so nothing else moves (the view mode's eye on an
        Explore wallpaper). Beside the row on a computer; above the stacked links on a phone. */}
      {extra && <div className="absolute top-3 right-4 sm:top-1/2 sm:right-6 sm:-translate-y-1/2">{extra}</div>}
      {wordmark && (
        <p
          aria-hidden
          className="-mb-[0.18em] bg-linear-to-b from-ink/40 to-ink/0 bg-clip-text pt-6 text-center text-[44vw] leading-[0.8] font-semibold tracking-[-0.07em] text-transparent select-none pointer-events-none sm:from-ink/30 sm:pt-8 sm:text-[30vw]"
        >
          {site.name.toLowerCase()}
        </p>
      )}
    </footer>
  );
}
