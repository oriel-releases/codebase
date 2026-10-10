"use client";

import { useState } from "react";

/**
 * A photo that arrives instead of popping in: once it has loaded it settles from a slight zoom and a soft blur to its
 * real size and sharpness (the `reveal` animation in globals.css). One image, a one-time animation: nothing keeps running.
 * Before it loads it is invisible, so the card's own background shows. `onLoaded` hands over the loaded image (its real
 * size, for a card that should take the photo's proportions).
 */
export function RevealImage({
  src,
  alt,
  className = "",
  onLoaded,
}: {
  src: string;
  alt: string;
  className?: string;
  onLoaded?: (img: HTMLImageElement) => void;
}) {
  const [loaded, setLoaded] = useState(false);
  const done = (img: HTMLImageElement) => {
    if (loaded) return;
    setLoaded(true);
    onLoaded?.(img);
  };
  return (
    // eslint-disable-next-line @next/next/no-img-element -- hotlinked Unsplash photos: there is no image server on static hosting
    <img
      src={src}
      alt={alt}
      // A photo already in the cache can finish before React hears about it.
      ref={(el) => {
        if (el?.complete && el.naturalWidth > 0) done(el);
      }}
      onLoad={(e) => done(e.currentTarget)}
      className={`${className} ${loaded ? "reveal" : "opacity-0"}`}
    />
  );
}
