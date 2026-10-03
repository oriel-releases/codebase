import { routes } from "@/config/site";
import type { Step } from "@/content/home";
import { ButtonLink } from "@/components/ui/Button";
import { Container } from "@/components/ui/Container";
import { DownloadIcon } from "@/components/ui/icons";
import { Section } from "@/components/ui/Section";
import { SectionHeading } from "@/components/ui/SectionHeading";
import { navigation } from "@/lib/transitions";

export function GetStarted({ steps }: { steps: Step[] }) {
  return (
    <Section id="get-started" labelledBy="get-started-title">
      <Container>
        <SectionHeading id="get-started-title">Get started in a minute</SectionHeading>
        <ol className="mt-12 grid gap-4 md:grid-cols-3">
          {steps.map((s, i) => (
            <li key={s.title} className="rounded-[2rem] border border-line p-7 sm:p-8">
              <span className="flex size-9 items-center justify-center rounded-full bg-ink text-sm font-medium text-paper">
                {i + 1}
              </span>
              <h3 className="mt-6 font-medium">{s.title}</h3>
              <p className="mt-2 text-muted">{s.body}</p>
            </li>
          ))}
        </ol>
        <div className="mt-12 flex justify-center">
          <ButtonLink href={routes.download} transitionTypes={[...navigation.forward]}>
            <DownloadIcon width={16} height={16} />
            Download Oriel
          </ButtonLink>
        </div>
      </Container>
    </Section>
  );
}
