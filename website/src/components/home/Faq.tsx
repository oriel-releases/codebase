import type { FaqItem } from "@/content/home";
import { Accordion } from "@/components/ui/Accordion";
import { Container } from "@/components/ui/Container";
import { Section } from "@/components/ui/Section";
import { SectionHeading } from "@/components/ui/SectionHeading";
import { SupportCard } from "@/components/ui/SupportCard";

export function Faq({ items }: { items: FaqItem[] }) {
  return (
    <Section labelledBy="faq">
      <Container className="max-w-2xl">
        <SectionHeading id="faq">Questions</SectionHeading>
        <div className="mt-12 space-y-3">
          <Accordion items={items.map((item) => ({ title: item.question, body: item.answer }))} />
        </div>
        <SupportCard className="mt-8" />
      </Container>
    </Section>
  );
}
