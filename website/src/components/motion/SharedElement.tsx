import { ViewTransition, type ReactNode } from "react";

/**
 * The same element on two pages (same `name`): it morphs between its old and new position and
 * size instead of disappearing and reappearing. `default="none"` keeps it still during
 * unrelated transitions.
 */
export function SharedElement({ name, children }: { name: string; children: ReactNode }) {
  return (
    <ViewTransition name={name} share="morph" default="none">
      {children}
    </ViewTransition>
  );
}
