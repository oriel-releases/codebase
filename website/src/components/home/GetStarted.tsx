import { routes, site } from "@/config/site";
import { hero, type Step } from "@/content/home";
import { StepScene } from "@/components/home/FeatureVisuals";
import { ButtonLink } from "@/components/ui/Button";
import { Container } from "@/components/ui/Container";
import { AndroidIcon, AppleIcon, DownloadIcon, LinuxIcon, WindowsIcon } from "@/components/ui/icons";
import { Logo } from "@/components/ui/Logo";
import { Section } from "@/components/ui/Section";
import { SectionHeading } from "@/components/ui/SectionHeading";
import { navigation } from "@/lib/transitions";

/**
 * Three steps, each shown before it's said: the systems to download for, the sign-in, and the first wallpaper arriving
 * (with the apps' own progress words). Cards like the features', the step's number on its scene. Under them, one long
 * card to download from: the app, the systems it runs on, and the button.
 */
export function GetStarted({ steps }: { steps: Step[] }) {
  return (
    <Section id="get-started" labelledBy="get-started-title">
      <Container className="lg:max-w-6xl">
        <SectionHeading id="get-started-title">Get started in a minute</SectionHeading>
        <ol className="mt-12 grid gap-3 sm:gap-4 md:grid-cols-3">
          {steps.map((s, i) => (
            <li key={s.title} className="flex flex-col rounded-[2rem] bg-surface p-2">
              <div className="relative h-48 overflow-hidden rounded-[1.5rem] bg-paper select-none">
                <div aria-hidden className="size-full">
                  <StepScene step={i} />
                </div>
                <span className="absolute top-3 right-3 flex size-8 items-center justify-center rounded-full bg-accent text-sm font-medium text-paper shadow-sm">
                  {i + 1}
                </span>
              </div>
              <div className="px-5 pt-5 pb-6 sm:px-6">
                <h3 className="font-medium">{s.title}</h3>
                <p className="mt-2 text-pretty text-muted">{s.body}</p>
              </div>
            </li>
          ))}
        </ol>
        <div className="mt-3 rounded-[2rem] bg-surface p-2 sm:mt-4">
          <div className="flex flex-col items-center gap-6 rounded-[1.5rem] bg-paper px-6 py-8 text-center md:flex-row md:gap-8 md:px-8 md:py-7 md:text-left">
            <Logo size={56} className="size-14 shrink-0" />
            <div className="flex-1">
              <p className="text-xl font-semibold tracking-tight">{site.name}</p>
              <p className="mt-1 text-sm text-pretty text-muted">{hero.eyebrow}</p>
            </div>
            <div aria-hidden className="flex gap-2">
              {[WindowsIcon, AppleIcon, LinuxIcon, AndroidIcon].map((Icon, i) => (
                <span key={i} className="grid size-11 place-items-center rounded-full bg-surface text-ink">
                  <Icon width={18} height={18} />
                </span>
              ))}
            </div>
            <ButtonLink
              href={routes.download}
              transitionTypes={[...navigation.forward]}
              className="w-full px-7 py-3.5 text-base md:w-auto"
            >
              <DownloadIcon width={17} height={17} />
              Download {site.name}
            </ButtonLink>
          </div>
        </div>
      </Container>
    </Section>
  );
}
