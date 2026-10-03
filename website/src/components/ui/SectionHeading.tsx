import type { ReactNode } from "react";

export function SectionHeading({ id, children }: { id: string; children: ReactNode }) {
  return (
    <h2 id={id} className="text-center text-2xl font-semibold tracking-tight sm:text-3xl">
      {children}
    </h2>
  );
}
