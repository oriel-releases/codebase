import Link from "next/link";
import { routes, site } from "@/config/site";
import { ButtonLink } from "@/components/ui/Button";
import { Container } from "@/components/ui/Container";
import { DownloadIcon } from "@/components/ui/icons";
import { Badge } from "@/components/ui/Badge";
import { Logo } from "@/components/ui/Logo";
import { navigation } from "@/lib/transitions";

/** Always on top; content scrolls underneath a frosted background. */
export function SiteHeader() {
  return (
    <header
      style={{ viewTransitionName: "site-header" }}
      className="fixed inset-x-0 top-0 z-50 border-b border-line/60 bg-paper/70 backdrop-blur-xl"
    >
      <Container className="flex h-16 items-center justify-between">
        <Link href={routes.home} transitionTypes={[...navigation.back]} className="flex items-center gap-2.5 rounded-full font-semibold tracking-tight">
          <Logo />
          {site.name}
          {site.stage && <Badge>{site.stage}</Badge>}
        </Link>
        <ButtonLink href={routes.download} transitionTypes={[...navigation.forward]} variant="secondary" size="sm">
          <DownloadIcon width={14} height={14} />
          Download
        </ButtonLink>
      </Container>
    </header>
  );
}
