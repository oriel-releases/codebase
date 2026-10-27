"use client";

import { Suspense, useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { Eye, EyeOff } from "lucide-react";

/**
 * The eye in the footer of an Explore wallpaper: it hides everything but the devices on their wallpaper (the header,
 * the footer, the page's buttons, the scrolling; see `data-view-only` in globals.css), and brings it all back, as does
 * Escape. Only the eye stays. It's for this visit only: leaving the wallpaper or reloading shows the page again.
 * Elsewhere on /explore there is no eye.
 */
export function ViewOnlyToggle() {
  return (
    <Suspense fallback={null}>
      <OnWallpaper />
    </Suspense>
  );
}

function OnWallpaper() {
  return useSearchParams().has("wallpaper") ? <Toggle /> : null;
}

function Toggle() {
  const [hidden, setHidden] = useState(false);

  useEffect(() => {
    const root = document.documentElement;
    if (hidden) root.dataset.viewOnly = "";
    else delete root.dataset.viewOnly;
    return () => {
      delete root.dataset.viewOnly;
    };
  }, [hidden]);

  // Escape shows the page again (and doesn't also leave the wallpaper: the page skips a handled Escape).
  useEffect(() => {
    if (!hidden) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      e.preventDefault();
      setHidden(false);
    };
    window.addEventListener("keydown", onKey, { capture: true });
    return () => window.removeEventListener("keydown", onKey, { capture: true });
  }, [hidden]);

  const Icon = hidden ? EyeOff : Eye;
  return (
    <button
      type="button"
      aria-pressed={hidden}
      aria-label={hidden ? "Show the page" : "Show only the wallpaper"}
      title={hidden ? "Show the page" : "Show only the wallpaper"}
      onClick={() => {
        if (!hidden) {
          // Hiding: back up to the top (the page may have been scrolled to this footer), then lock the scroll, and move
          // the devices to the middle of the screen: how far, measured from where they'll be once scrolled up.
          window.scrollTo({ top: 0, behavior: "smooth" });
          const stage = document.querySelector("[data-view-grow]");
          if (stage) {
            const box = stage.getBoundingClientRect();
            const middle = box.top + window.scrollY + box.height / 2;
            document.documentElement.style.setProperty("--view-shift", `${window.innerHeight / 2 - middle}px`);
          }
        }
        setHidden((h) => !h);
      }}
      className={`grid size-9 shrink-0 place-items-center rounded-full transition-[color,background-color,box-shadow,scale] duration-300 ease-out hover:text-ink active:scale-95 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent ${
        // Hidden, the page scrolls up and the footer leaves view: the eye stays reachable in the screen's corner.
        hidden
          ? "fixed right-4 bottom-4 z-50 size-11 bg-paper/70 text-ink shadow-lg ring-1 ring-line backdrop-blur-md sm:right-6 sm:bottom-6"
          : "hover:bg-surface"
      }`}
    >
      <Icon size={17} aria-hidden />
    </button>
  );
}
