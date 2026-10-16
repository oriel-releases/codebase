/** A link on the new tab, under the clock. Synced with the account (users/{uid}/sync/extension) when one is linked. */
export interface Shortcut {
  title: string;
  url: string;
}

/** As many as the account keeps (website/firestore.rules allows 14). */
export const maxShortcuts = 14;

// Plain localStorage (same keys as settings.ts remember()), so this file has no imports and tests run it as is.
function read<T>(name: string): T | null {
  try {
    return JSON.parse(localStorage.getItem(`oriel.${name}`) ?? "null");
  } catch {
    return null;
  }
}

function write(name: string, value: unknown): void {
  try {
    localStorage.setItem(`oriel.${name}`, JSON.stringify(value));
  } catch {
    // Storage blocked: it just won't persist.
  }
}

export function loadShortcuts(): Shortcut[] {
  const items = read<Shortcut[]>("shortcuts");
  return Array.isArray(items) ? items.filter((s) => s && typeof s.url === "string").slice(0, maxShortcuts) : [];
}

/** Saves the list here. `fromSync` is for a list that just came from the account, so it isn't sent straight back. */
export function saveShortcuts(items: Shortcut[], fromSync = false): void {
  write("shortcuts", items.slice(0, maxShortcuts));
  if (!fromSync) markUnsynced();
}

/** Something synced (settings or shortcuts) changed here and should go up to the account. */
export function markUnsynced(): void {
  write("ext-dirty", Date.now());
}

/** "github.com/x" → "https://github.com/x". Only web links; anything else is null. */
export function normalizeUrl(input: string): string | null {
  const text = input.trim();
  if (!text || /\s/.test(text)) return null;
  try {
    const url = new URL(/^[a-z][a-z0-9+.-]*:/i.test(text) ? text : `https://${text}`);
    if (url.protocol !== "http:" && url.protocol !== "https:") return null;
    if (!url.hostname.includes(".") && url.hostname !== "localhost") return null;
    return url.href;
  } catch {
    return null;
  }
}

/**
 * The same page, however it was written: http or https, with or without "www.", a trailing slash or a #fragment.
 * Used to tell whether the page you're on is already a shortcut.
 */
export function sameLink(a: string, b: string): boolean {
  const key = (url: string) => {
    try {
      const u = new URL(url);
      return `${u.hostname.replace(/^www\./, "")}${u.pathname.replace(/\/+$/, "")}${u.search}`;
    } catch {
      return url;
    }
  };
  return key(a) === key(b);
}

/** A name from the address when none is given: "www.github.com" → "Github". */
export function titleFor(url: string): string {
  try {
    const host = new URL(url).hostname.replace(/^www\./, "");
    const name = host.split(".").slice(0, -1).join(".") || host;
    return name.charAt(0).toUpperCase() + name.slice(1);
  } catch {
    return url;
  }
}

/** The site's own icon, asked from the site itself (no icon service in between). */
export function siteIconUrl(url: string): string | null {
  try {
    return `${new URL(url).origin}/favicon.ico`;
  } catch {
    return null;
  }
}

/** Chrome's own favicon cache (the "favicon" permission); Firefox has none, so its tiles show the first letter. */
export function faviconUrl(url: string): string | null {
  if (/Firefox\//.test(navigator.userAgent)) return null;
  return chrome.runtime.getURL(`/_favicon/?pageUrl=${encodeURIComponent(url)}&size=64`);
}
