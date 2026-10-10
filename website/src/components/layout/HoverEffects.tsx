"use client";

import { useEffect } from "react";

/**
 * Feeds the hover effects in globals.css: while the pointer moves over an element marked data-spotlight (a soft glow that
 * follows it) or data-tilt (a gentle 3D lean toward it), the pointer's position is written to CSS variables. One listener
 * for the whole site; nothing happens on touch screens.
 */
export function HoverEffects() {
  useEffect(() => {
    if (!window.matchMedia("(hover: hover) and (pointer: fine)").matches) return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

    const onMove = (event: PointerEvent) => {
      const el = (event.target as Element | null)?.closest<HTMLElement>("[data-spotlight], [data-tilt]");
      if (!el) return;
      const box = el.getBoundingClientRect();
      const x = event.clientX - box.left;
      const y = event.clientY - box.top;
      el.style.setProperty("--mx", `${x}px`);
      el.style.setProperty("--my", `${y}px`);
      if (el.hasAttribute("data-tilt")) {
        el.style.setProperty("--rx", `${(x / box.width - 0.5) * 7}deg`);
        el.style.setProperty("--ry", `${-(y / box.height - 0.5) * 7}deg`);
      }
    };
    document.addEventListener("pointermove", onMove, { passive: true });
    return () => document.removeEventListener("pointermove", onMove);
  }, []);

  return null;
}
