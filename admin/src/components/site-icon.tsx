"use client";

import { useState } from "react";

/**
 * A shortcut's site icon: Google's favicon service first (it knows nearly every site), then the site's own
 * /favicon.ico, then the first letter, which is what the extension shows when it has no icon either.
 */
export function SiteIcon({ url, label }: { url?: string; label: string }) {
  const sources = iconSources(url);
  const [tried, setTried] = useState(0);
  const src = sources[tried];
  return (
    <span className="grid size-8 shrink-0 place-items-center overflow-hidden rounded-full bg-secondary text-xs font-medium uppercase">
      {src ? (
        <img
          key={src}
          src={src}
          alt=""
          className="size-5 object-contain"
          referrerPolicy="no-referrer"
          onError={() => setTried((t) => t + 1)}
        />
      ) : (
        (label || "?")[0]
      )}
    </span>
  );
}

function iconSources(url?: string): string[] {
  try {
    const { hostname, origin } = new URL(url ?? "");
    return [`https://www.google.com/s2/favicons?domain=${encodeURIComponent(hostname)}&sz=64`, `${origin}/favicon.ico`];
  } catch {
    return [];
  }
}
