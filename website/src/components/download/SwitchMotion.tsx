"use client";

import { useLayoutEffect, useRef, useState, type ReactNode } from "react";

/**
 * How a Windows / Linux / Android (or browser) switch moved last, for the animations of what changes with it: nothing
 * before the first switch (the page's own entrance stays alone), then toward the right or the left.
 */
export function useSwitchMotion<K>(order: readonly K[]) {
  const [dir, setDir] = useState<0 | 1 | -1>(0);
  return {
    change(from: K, to: K, set: (key: K) => void) {
      if (from === to) return;
      setDir(order.indexOf(to) > order.indexOf(from) ? 1 : -1);
      set(to);
    },
    /** Text sliding in from the side that was picked. Put it on an element keyed by the option, so it plays again. */
    textIn:
      dir === 0
        ? ""
        : dir > 0
          ? "animate-[swap-in-next_0.4s_cubic-bezier(0.2,0.8,0.2,1)] motion-reduce:animate-none"
          : "animate-[swap-in-prev_0.4s_cubic-bezier(0.2,0.8,0.2,1)] motion-reduce:animate-none",
    /** The icon turning in: clockwise when moving right, the other way when moving left. */
    iconIn:
      dir === 0
        ? ""
        : dir > 0
          ? "animate-[spin-in-next_0.55s_cubic-bezier(0.2,0.8,0.2,1)] motion-reduce:animate-none"
          : "animate-[spin-in-prev_0.55s_cubic-bezier(0.2,0.8,0.2,1)] motion-reduce:animate-none",
  };
}

/**
 * Its width follows its content smoothly: "Download for Linux" grows into "Download for Android" instead of jumping.
 * The content is measured as it is (ResizeObserver) and the box animates to that width.
 */
export function SmoothWidth({ children }: { children: ReactNode }) {
  const inner = useRef<HTMLSpanElement>(null);
  const [width, setWidth] = useState<number>();

  useLayoutEffect(() => {
    const el = inner.current;
    if (!el) return;
    const measure = () => setWidth(el.offsetWidth);
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  return (
    <span
      className="-mx-1 inline-flex overflow-hidden transition-[width] duration-[450ms] ease-[cubic-bezier(0.2,0.8,0.2,1)] motion-reduce:transition-none"
      style={{ width }}
    >
      {/* A little room on both sides so the turning icon's corners aren't clipped. */}
      <span ref={inner} className="inline-flex shrink-0 items-center gap-2.5 px-1 whitespace-nowrap">
        {children}
      </span>
    </span>
  );
}
