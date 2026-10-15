import { deleteDoc, doc, getDoc, serverTimestamp, setDoc, updateDoc, type Timestamp } from "firebase/firestore";
import { firestore } from "@/lib/firebase";

/**
 * One document per signed-in app, browser extension or website visit, in `users/{uid}/sessions`. The apps keep their own;
 * this is the website's. Deleting a session signs that device out the next time it checks in.
 */
export type SessionRow = {
  id: string;
  platform: string;
  deviceName: string;
  appVersion: string;
  createdAt: Date | null;
  lastSeenAt: Date | null;
};

const sessionKey = "oriel.web-session";
const signedInKey = "oriel.signed-in";

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string | null) {
  try {
    if (value === null) localStorage.removeItem(key);
    else localStorage.setItem(key, value);
  } catch {
    // Storage blocked (private mode): the page still works, it just won't remember this device.
  }
}

/** This browser's session id on the website, if it has one. */
export function localSessionId(): string | null {
  return read(sessionKey);
}

/** Whether this browser looks signed in; cheap, so the header can decide without loading Firebase. */
export function looksSignedIn(): boolean {
  return read(signedInKey) === "1";
}

export function subscribeSignedIn(callback: () => void): () => void {
  window.addEventListener("storage", callback);
  window.addEventListener("oriel:signed-in-changed", callback);
  return () => {
    window.removeEventListener("storage", callback);
    window.removeEventListener("oriel:signed-in-changed", callback);
  };
}

export function markSignedIn(signedIn: boolean) {
  write(signedInKey, signedIn ? "1" : null);
  window.dispatchEvent(new Event("oriel:signed-in-changed"));
}

/** Forgets this browser's session after signing out. */
export function forgetWebSession() {
  write(sessionKey, null);
  markSignedIn(false);
}

/** Makes sure this browser has a live session document (reusing its own while it still exists) and notes the visit. Returns its id. */
export async function ensureWebSession(uid: string): Promise<string> {
  const db = firestore();
  const existing = localSessionId();
  if (existing) {
    const ref = doc(db, "users", uid, "sessions", existing);
    if ((await getDoc(ref)).exists()) {
      await updateDoc(ref, { lastSeenAt: serverTimestamp() });
      return existing;
    }
  }
  const id = crypto.randomUUID().replaceAll("-", "");
  await setDoc(doc(db, "users", uid, "sessions", id), {
    platform: "web",
    deviceName: browserLabel(),
    appVersion: "",
    createdAt: serverTimestamp(),
    lastSeenAt: serverTimestamp(),
  });
  write(sessionKey, id);
  markSignedIn(true);
  return id;
}

export async function endSession(uid: string, sessionId: string) {
  await deleteDoc(doc(firestore(), "users", uid, "sessions", sessionId));
}

export function toSessionRow(id: string, data: Record<string, unknown>): SessionRow {
  const date = (value: unknown) => (value && typeof (value as Timestamp).toDate === "function" ? (value as Timestamp).toDate() : null);
  return {
    id,
    platform: String(data.platform ?? ""),
    deviceName: String(data.deviceName ?? ""),
    appVersion: String(data.appVersion ?? ""),
    createdAt: date(data.createdAt),
    lastSeenAt: date(data.lastSeenAt),
  };
}

/** "Chrome on Windows": a short description of this browser for the session list. */
export function browserLabel(): string {
  const ua = navigator.userAgent;
  const browser = ua.includes("Firefox/") ? "Firefox" : ua.includes("Edg/") ? "Edge" : ua.includes("OPR/") ? "Opera" : ua.includes("Chrome/") ? "Chrome" : ua.includes("Safari/") ? "Safari" : "Browser";
  const os = /Windows/.test(ua) ? "Windows" : /Android/.test(ua) ? "Android" : /iPhone|iPad/.test(ua) ? "iOS" : /Mac OS X/.test(ua) ? "macOS" : /Linux/.test(ua) ? "Linux" : "";
  return os ? `${browser} on ${os}` : browser;
}

export const platformNames: Record<string, string> = {
  windows: "Windows app",
  macos: "Mac app",
  linux: "Linux app",
  android: "Android app",
  extension: "Browser extension",
  web: "Website",
};

/** "5 minutes ago", "yesterday", "3 Oct". */
export function timeAgo(date: Date | null, now = Date.now()): string {
  if (!date) return "";
  const seconds = Math.round((date.getTime() - now) / 1000);
  const rtf = new Intl.RelativeTimeFormat("en", { numeric: "auto" });
  const steps: [Intl.RelativeTimeFormatUnit, number][] = [["minute", 60], ["hour", 3600], ["day", 86400]];
  if (Math.abs(seconds) < 60) return "just now";
  for (let i = steps.length - 1; i >= 0; i--) {
    const [unit, size] = steps[i];
    if (Math.abs(seconds) >= size) return Math.abs(seconds) >= 86400 * 30 ? shortDate(date) : rtf.format(Math.round(seconds / size), unit);
  }
  return shortDate(date);
}

export function shortDate(date: Date | null): string {
  return date ? date.toLocaleDateString("en", { day: "numeric", month: "short", year: "numeric" }) : "";
}
