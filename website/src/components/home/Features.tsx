import type { Feature } from "@/content/home";
import { Container } from "@/components/ui/Container";
import { Section } from "@/components/ui/Section";
import { SectionHeading } from "@/components/ui/SectionHeading";

export function Features({ items }: { items: Feature[] }) {
  return (
    <Section labelledBy="features">
      <Container>
        <SectionHeading id="features">Everything it does, quietly</SectionHeading>
        <ul className="mt-12 grid gap-4 sm:grid-cols-2">
          {items.map((f) => (
            <li key={f.title} data-spotlight className="rounded-[2rem] bg-surface p-7 transition-[translate,box-shadow,background-color] duration-300 ease-out hover:-translate-y-1 hover:bg-line/50 hover:shadow-xl hover:shadow-ink/5 motion-reduce:transition-none motion-reduce:hover:translate-y-0 sm:p-8">
              <h3 className="font-medium">{f.title}</h3>
              <p className="mt-2 text-muted">{f.body}</p>
            </li>
          ))}
        </ul>
      </Container>
    </Section>
  );
}
