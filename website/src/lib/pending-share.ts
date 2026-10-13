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
    localStorage.removeItem(followKey);
  } catch {
    // nothing to forget
  }
}

/** A friend's follow link opened before installing Oriel: followed once the new app signs in. */
const followKey = "oriel.pending-follow";

export function rememberFollow(code: string): void {
  try {
    localStorage.setItem(followKey, JSON.stringify({ id: code, from: "", at: Date.now() } satisfies Pending));
  } catch {
    // Private mode or storage turned off: the link still works for anyone who has the app.
  }
}

/**
 * "&share=ID&from=Name" and/or "&follow=CODE" for the sign-in link, or "" when nothing (recent) is waiting. Both are
 * forgotten once the link is made (see forgetShare).
 */
export function pendingShareQuery(): string {
  let query = "";
  try {
    const pending = JSON.parse(localStorage.getItem(key) ?? "null") as Pending | null;
    if (pending && Date.now() - pending.at <= maxAge && /^[A-Za-z0-9_-]{6,24}$/.test(pending.id))
      query += `&share=${encodeURIComponent(pending.id)}${pending.from ? `&from=${encodeURIComponent(pending.from)}` : ""}`;
    const follow = JSON.parse(localStorage.getItem(followKey) ?? "null") as Pending | null;
    if (follow && Date.now() - follow.at <= maxAge && followCode.test(follow.id)) query += `&follow=${follow.id}`;
  } catch {
    // Unreadable: nothing comes along.
  }
  return query;
}

/** A follow code: 8 letters and numbers, never O, 0, I or 1. */
export const followCode = /^[A-HJ-NP-Z2-9]{8}$/;
