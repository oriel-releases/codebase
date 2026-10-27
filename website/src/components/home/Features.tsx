import type { Feature } from "@/content/home";
import { FeatureScene } from "@/components/home/FeatureVisuals";
import { Container } from "@/components/ui/Container";
import { Section } from "@/components/ui/Section";
import { SectionHeading } from "@/components/ui/SectionHeading";

/** The wide cards in the bento: two columns each, the rest one, so every row of three is full on a computer. */
const wide = new Set(["devices", "topics", "browser"]);

/**
 * What Oriel does, as a bento of cards: each shows its idea in a small scene (FeatureVisuals), then says it in a line.
 * Three cards are wide, so the rows of three line up; on a tablet the grid packs them two by two.
 */
export function Features({ items }: { items: Feature[] }) {
  return (
    <Section labelledBy="features">
      <Container className="lg:max-w-6xl">
        <SectionHeading id="features">Everything it does, quietly</SectionHeading>
        <ul className="mt-12 grid gap-3 sm:gap-4 md:grid-flow-dense md:grid-cols-2 lg:grid-cols-3">
          {items.map((f) => (
            <li
              key={f.title}
              className={`flex flex-col rounded-[2rem] bg-surface p-2 ${f.visual && wide.has(f.visual) ? "md:col-span-2" : ""}`}
            >
              {f.visual && (
                <div aria-hidden className="relative h-48 overflow-hidden rounded-[1.5rem] bg-paper select-none">
                  <FeatureScene visual={f.visual} />
                </div>
              )}
              <div className="px-5 pt-5 pb-6 sm:px-6">
                <h3 className="font-medium">{f.title}</h3>
                <p className="mt-2 text-pretty text-muted">{f.body}</p>
              </div>
            </li>
          ))}
        </ul>
      </Container>
    </Section>
  );
}
