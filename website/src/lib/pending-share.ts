/**
 * A shared wallpaper that should survive installing Oriel. Someone opens a share link without the app: the page
 * remembers the photo here, they install Oriel and sign in on /login, and the sign-in link hands the photo to the app
 * (`oriel://auth?token=...&share=ID&from=Name`), which offers it right after setup. Only this browser keeps it, for a
 * few days, and it is used once.
 */

const key = "oriel.pending-share";
const maxAge = 3 * 24 * 60 * 60 * 1000;

type Pending = { id: string; from: string; at: number };

export function rememberShare(id: string, from: string): void {
  try {
    localStorage.setItem(key, JSON.stringify({ id, from, at: Date.now() } satisfies Pending));
  } catch {
    // Private mode or storage turned off: the link still works for anyone who has the app.
  }
}

export function forgetShare(): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // nothing to forget
  }
}

/** "&share=ID&from=Name" for the sign-in link, or "" when nothing (recent) is waiting. */
export function pendingShareQuery(): string {
  try {
    const pending = JSON.parse(localStorage.getItem(key) ?? "null") as Pending | null;
    if (!pending || Date.now() - pending.at > maxAge || !/^[A-Za-z0-9_-]{6,24}$/.test(pending.id)) return "";
    return `&share=${encodeURIComponent(pending.id)}${pending.from ? `&from=${encodeURIComponent(pending.from)}` : ""}`;
  } catch {
    return "";
  }
}
