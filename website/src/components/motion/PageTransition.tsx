import { ViewTransition, type ReactNode } from "react";

const directional = { "nav-forward": "nav-forward", "nav-back": "nav-back", default: "none" };

/**
 * Wrap a page's content (in page.tsx, not the layout: layouts persist, so they never enter or
 * exit). Slides left or right depending on the link's transition type; untyped navigations
 * (browser back, refresh) don't animate.
 */
export function PageTransition({ children }: { children: ReactNode }) {
  return (
    <ViewTransition enter={directional} exit={directional} default="none">
      {children}
    </ViewTransition>
  );
}
