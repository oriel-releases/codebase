import type { FaqItem } from "@/content/home";
import { AccordionItem } from "@/components/ui/Accordion";
import { Container } from "@/components/ui/Container";
import { Section } from "@/components/ui/Section";
import { SectionHeading } from "@/components/ui/SectionHeading";

export function Faq({ items }: { items: FaqItem[] }) {
  return (
    <Section labelledBy="faq">
      <Container className="max-w-2xl">
        <SectionHeading id="faq">Questions</SectionHeading>
        <div className="mt-12 space-y-3">
          {items.map((item) => (
            <AccordionItem key={item.question} title={item.question}>
              {item.answer}
            </AccordionItem>
          ))}
        </div>
      </Container>
    </Section>
  );
}
