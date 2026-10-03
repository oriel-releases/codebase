import type { ReactNode } from "react";

/** Small yellow pill, e.g. the "Beta" stage marker next to the name. */
export function Badge({ children }: { children: ReactNode }) {
  return (
    <span className="rounded-full bg-beta px-2 py-0.5 text-[11px] leading-4 font-medium tracking-normal text-beta-ink">
      {children}
    </span>
  );
}
