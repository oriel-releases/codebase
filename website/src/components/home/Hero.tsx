import { routes } from "@/config/site";
import { hero } from "@/content/home";
import { AnchorButton, ButtonLink } from "@/components/ui/Button";
import { Container } from "@/components/ui/Container";
import { ArrowDownIcon, DownloadIcon } from "@/components/ui/icons";
import { Logo } from "@/components/ui/Logo";
import { Section } from "@/components/ui/Section";
import { SharedElement } from "@/components/motion/SharedElement";
import { navigation, sharedElement } from "@/lib/transitions";

export function Hero() {
  return (
    <Section className="pt-28">
      <Container className="flex flex-col items-center text-center">
        <SharedElement name={sharedElement.logo}>
          <Logo size={96} priority className="size-20 sm:size-24" />
        </SharedElement>
        <p className="mt-10 rounded-full bg-surface px-4 py-1.5 text-xs font-medium text-accent sm:mt-12">{hero.eyebrow}</p>
        <h1 className="mt-6 max-w-2xl text-4xl font-semibold tracking-tight text-balance sm:text-6xl">{hero.title}</h1>
        <p className="mt-6 max-w-xl text-base text-pretty text-muted sm:text-lg">{hero.body}</p>
        <div className="mt-10 flex flex-wrap justify-center gap-2.5 sm:mt-12">
          <SharedElement name={sharedElement.downloadButton}>
            <ButtonLink href={routes.download} transitionTypes={[...navigation.forward]}>
              <DownloadIcon width={16} height={16} />
              Download Oriel
            </ButtonLink>
          </SharedElement>
          <AnchorButton href="#get-started" variant="secondary">
            <ArrowDownIcon width={14} height={14} />
            Get started
          </AnchorButton>
        </div>
      </Container>
    </Section>
  );
}
