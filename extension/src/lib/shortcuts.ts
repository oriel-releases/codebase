/** A link on the new tab, under the clock. Synced with the account (users/{uid}/sync/extension) when one is linked. */
export interface Link {
  title: string;
  url: string;
}

/** Several links under one tile: opens as a small window over the page (newtab/folder-view.ts). */
export interface Folder {
  title: string;
  items: Link[];
}

/** One tile in the row: a link or a folder of links. */
export type Shortcut = Link | Folder;

/** Tiles in the row, folders counted as one (website/firestore.rules allows 14). */
export const maxShortcuts = 14;
/** Links in one folder. */
export const maxInFolder = 12;

export const isFolder = (s: Shortcut): s is Folder => "items" in s;

/** Every link, the ones in folders too (to tell whether a page is already a shortcut). */
export const allLinks = (list: Shortcut[]): Link[] => list.flatMap((s) => (isFolder(s) ? s.items : [s]));

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

/**
 * Only what this version understands, in the shape it keeps: links with a web address and folders of them (a folder
 * always holds at least one: an empty one is dropped), within the limits. Used for what's stored here and for what the
 * account sends.
 */
export function cleanShortcuts(raw: unknown): Shortcut[] {
  if (!Array.isArray(raw)) return [];
  const link = (x: unknown): Link | null =>
    x && typeof x === "object" && typeof (x as Link).url === "string" ? { title: String((x as Link).title ?? ""), url: (x as Link).url } : null;
  const out: Shortcut[] = [];
  for (const x of raw) {
    if (x && typeof x === "object" && Array.isArray((x as Folder).items)) {
      const items = (x as Folder).items.map(link).filter((l): l is Link => !!l).slice(0, maxInFolder);
      if (items.length) out.push({ title: String((x as Folder).title ?? "") || "Folder", items });
    } else {
      const l = link(x);
      if (l) out.push(l);
    }
  }
  return out.slice(0, maxShortcuts);
}

export function loadShortcuts(): Shortcut[] {
  return cleanShortcuts(read("shortcuts"));
}

/** Saves the list here. `fromSync` is for a list that just came from the account, so it isn't sent straight back. */
export function saveShortcuts(items: Shortcut[], fromSync = false): void {
  write("shortcuts", cleanShortcuts(items));
  if (!fromSync) markUnsynced();
}

// ---- Folders: every change returns a new list (the row redraws from it).

/**
 * Drops link `from` onto tile `onto`: onto a folder it joins at the end (if there's room); onto a link the two become
 * a new folder in that link's place. Returns the new list and where the folder is, or null when it can't be done.
 */
export function combine(list: Shortcut[], from: number, onto: number): { list: Shortcut[]; folder: number } | null {
  const moving = list[from];
  const target = list[onto];
  if (!moving || !target || from === onto || isFolder(moving)) return null;
  if (isFolder(target) && target.items.length >= maxInFolder) return null;
  const merged: Folder = isFolder(target) ? { ...target, items: [...target.items, moving] } : { title: "Folder", items: [target, moving] };
  const next = list.map((s, i) => (i === onto ? merged : s)).filter((_, i) => i !== from);
  return { list: next, folder: onto > from ? onto - 1 : onto };
}

/** Changes folder `f` (rename, reorder, edit a link). A folder left with no links goes away: none is ever empty. */
export function updateFolder(list: Shortcut[], f: number, change: (folder: Folder) => Folder): Shortcut[] {
  return list.flatMap((s, i) => {
    if (i !== f || !isFolder(s)) return [s];
    const next = change(s);
    return next.items.length ? [next] : [];
  });
}

/** Takes link `i` out of folder `f` and puts it in the row right after the folder; the last one out takes its place. */
export function takeOut(list: Shortcut[], f: number, i: number): Shortcut[] {
  const folder = list[f];
  if (!folder || !isFolder(folder) || !folder.items[i]) return list;
  const rest = folder.items.filter((_, k) => k !== i);
  if (rest.length && list.length >= maxShortcuts) return list; // the row has no room for it
  return [...list.slice(0, f), ...(rest.length ? [{ ...folder, items: rest }] : []), folder.items[i], ...list.slice(f + 1)];
}

/**
 * A new folder from links already in the row (`picked`, their places in the list): it takes the place of the first
 * one picked. Returns the list and where the folder is; null with nothing picked (a folder is never empty).
 */
export function makeFolder(list: Shortcut[], title: string, picked: number[]): { list: Shortcut[]; folder: number } | null {
  // In the row's order, whatever order they were picked in.
  const links = [...picked].sort((a, b) => a - b).map((i) => list[i]).filter((s): s is Link => !!s && !isFolder(s)).slice(0, maxInFolder);
  if (!links.length) return null;
  const folder: Folder = { title: title.trim() || "Folder", items: links };
  // The first picked link's place: nothing picked comes before it, so taking the picked ones out doesn't move it.
  const at = Math.min(...picked);
  const next = list.filter((s) => !links.includes(s as Link));
  next.splice(at, 0, folder);
  return { list: next, folder: at };
}

/** Deletes folder `f`; with `keepLinks` its links stay, in its place in the row (as far as there's room). */
export function deleteFolder(list: Shortcut[], f: number, keepLinks: boolean): Shortcut[] {
  return keepLinks ? ungroup(list, f) : list.filter((_, i) => i !== f);
}

/** Ungroup: the folder's links take its place in the row, as far as the row has room (the rest stay in the folder). */
export function ungroup(list: Shortcut[], f: number): Shortcut[] {
  const folder = list[f];
  if (!folder || !isFolder(folder)) return list;
  const room = maxShortcuts - list.length + 1;
  const out = folder.items.slice(0, room);
  const kept = folder.items.slice(room);
  return [...list.slice(0, f), ...out, ...(kept.length ? [{ ...folder, items: kept }] : []), ...list.slice(f + 1)];
}

/** Something synced (settings or shortcuts) changed here and should go up to the account. */
export function markUnsynced(): void {
  write("ext-dirty", Date.now());
}

/**
 * "github.com/x" → "https://github.com/x", and a file on this computer ("file:///Users/me/a.pdf", "/Users/me/a.pdf",
 * "C:\Books\a.pdf") → its file:// link. Anything else is null.
 */
export function normalizeUrl(input: string): string | null {
  const text = input.trim();
  if (!text) return null;
  const file = fileLink(text);
  if (file !== undefined) return file;
  if (/\s/.test(text)) return null;
  try {
    const url = new URL(/^[a-z][a-z0-9+.-]*:/i.test(text) ? text : `https://${text}`);
    if (url.protocol !== "http:" && url.protocol !== "https:") return null;
    if (!url.hostname.includes(".") && url.hostname !== "localhost") return null;
    return url.href;
  } catch {
    return null;
  }
}

/** A file path or file:// address as a file:// link (spaces and all); undefined when the text isn't one. */
function fileLink(text: string): string | null | undefined {
  let href: string;
  if (/^file:/i.test(text)) href = text;
  else if (text.startsWith("/")) href = `file://${text}`;
  else if (/^[a-z]:[\\/]/i.test(text)) href = `file:///${text.replaceAll("\\", "/")}`;
  else return undefined;
  try {
    const url = new URL(href);
    return url.protocol === "file:" && url.pathname.length > 1 ? url.href : null;
  } catch {
    return null;
  }
}

export const isFileLink = (url: string) => url.startsWith("file:");

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

/** A name from the address when none is given: "www.github.com" → "Github", "file:///…/Grokking.pdf" → "Grokking". */
export function titleFor(url: string): string {
  try {
    if (isFileLink(url)) {
      const name = decodeURIComponent(new URL(url).pathname.replace(/\/+$/, "").split("/").pop() ?? "");
      return name.replace(/(.)\.[^.]+$/, "$1") || name;
    }
    const host = new URL(url).hostname.replace(/^www\./, "");
    const name = host.split(".").slice(0, -1).join(".") || host;
    return name.charAt(0).toUpperCase() + name.slice(1);
  } catch {
    return url;
  }
}

/** The site's own icon, asked from the site itself (no icon service in between). */
export function siteIconUrl(url: string): string | null {
  if (isFileLink(url)) return null; // a file has no site: its tile shows the first letter
  try {
    return `${new URL(url).origin}/favicon.ico`;
  } catch {
    return null;
  }
}

/** Chrome's own favicon cache (the "favicon" permission); Firefox has none, so its tiles show the first letter. */
export function faviconUrl(url: string): string | null {
  if (/Firefox\//.test(navigator.userAgent) || isFileLink(url)) return null;
  return chrome.runtime.getURL(`/_favicon/?pageUrl=${encodeURIComponent(url)}&size=64`);
}
