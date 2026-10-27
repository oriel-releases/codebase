import type { ReactNode } from "react";

type Props = {
  children: ReactNode;
  /** Anchor target for in-page links; the whole section scrolls into view. */
  id?: string;
  labelledBy?: string;
  /** A name for a section with no heading of its own. */
  label?: string;
  className?: string;
};

/** A full-screen section with its content centered vertically. */
export function Section({ children, id, labelledBy, label, className = "" }: Props) {
  return (
    <section id={id} aria-labelledby={labelledBy} aria-label={label} className={`flex min-h-svh items-center py-24 ${className}`}>
      {children}
    </section>
  );
}
