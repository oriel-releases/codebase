"use client";

import { useId, useState } from "react";
import { PlusIcon } from "@/components/ui/icons";

type Props = { title: string; children: string; open: boolean; onToggle: () => void };

/**
 * One expandable row. The height animates via grid-template-rows (0fr -> 1fr), which works in
 * every browser. The answer stays in the HTML when closed, so it's still crawlable.
 */
export function AccordionItem({ title, children, open, onToggle }: Props) {
  const panelId = useId();

  return (
    <div className="group rounded-[2rem] bg-surface transition-colors duration-200 ">
      <h3>
        <button
          type="button"
          aria-expanded={open}
          aria-controls={panelId}
          onClick={onToggle}
          className="flex w-full items-center justify-between gap-4 rounded-[2rem] px-6 py-5 text-left font-medium focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent sm:px-7"
        >
          {title}
          <span
            className={`flex size-8 shrink-0 items-center justify-center rounded-full bg-paper text-muted transition-[rotate,color] duration-300 ease-out motion-reduce:transition-none ${open ? "rotate-45" : ""}`}
          >
            <PlusIcon width={16} height={16} />
          </span>
        </button>
      </h3>
      <div
        id={panelId}
        role="region"
        inert={!open}
        className={`grid transition-[grid-template-rows] duration-300 ease-out ${open ? "grid-rows-[1fr]" : "grid-rows-[0fr]"}`}
      >
        <div className="overflow-hidden">
          <p className="px-6 pb-6 text-muted sm:px-7">{children}</p>
        </div>
      </div>
    </div>
  );
}

/** The rows together, only one open at a time: opening one closes the other. */
export function Accordion({ items }: { items: { title: string; body: string }[] }) {
  const [open, setOpen] = useState<number | null>(null);
  return items.map((item, i) => (
    <AccordionItem key={item.title} title={item.title} open={open === i} onToggle={() => setOpen((o) => (o === i ? null : i))}>
      {item.body}
    </AccordionItem>
  ));
}
