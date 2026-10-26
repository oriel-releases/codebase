// Everything that talks to Firebase. It is imported lazily (see main.ts) so a new tab for someone
// who never linked an account doesn't load the SDK at all.
import { initializeApp } from "firebase/app";
import {
  GoogleAuthProvider,
  getAuth,
  onAuthStateChanged,
  sendPasswordResetEmail,
  signInWithCredential,
  signInWithEmailAndPassword,
  signOut,
  type User,
} from "firebase/auth/web-extension";
// Firestore "lite" (plain requests, a fraction of the full SDK's size): live updates come from the Realtime Database
// doorbell instead of a Firestore listener, so a new tab loads much less code.
import { deleteDoc, doc, getDoc, getFirestore, serverTimestamp, setDoc, setLogLevel, updateDoc } from "firebase/firestore/lite";
import { isBirthday } from "./age";
import { firebaseConfig, unsplashRef } from "./config";
import type { Photo } from "./photo";
import { remember, type Settings } from "./settings";
import { cleanShortcuts, isFolder, type Shortcut } from "./shortcuts";

const app = initializeApp(firebaseConfig);
const auth = getAuth(app);
const db = getFirestore(app);
// Every Firestore call here handles its own failure (offline, a revoked session): it retries later or tells the person.
// The SDK would also print each one to the console, where the browser lists it among the extension's errors.
setLogLevel("silent");

export interface Profile {
  uid: string;
  email: string;
  name: string;
  photo: string;
}

/** How often to confirm this sign-in wasn't revoked (the desktop apps do the same every 6 hours). */
const verifyEvery = 6 * 60 * 60 * 1000;

export function cachedProfile(): Profile | null {
  return remember<Profile>("profile");
}

/** Reports the signed-in user (or null) as soon as Firebase has restored the session. */
export function watchUser(callback: (user: Profile | null) => void): () => void {
  return onAuthStateChanged(auth, (user) => {
    const profile = user ? toProfile(user) : null;
    remember("profile", profile);
    callback(profile);
  });
}

function toProfile(user: User): Profile {
  return { uid: user.uid, email: user.email ?? "", name: user.displayName ?? "", photo: user.photoURL ?? "" };
}

/** Finishes a sign-in started on the Oriel website: exchanges its Google credential for this browser's own session. */
export async function signInWithGoogleToken(idToken: string): Promise<Profile> {
  const { user } = await signInWithCredential(auth, GoogleAuthProvider.credential(idToken));
  return recordSession(user);
}

/** Signs in with an Oriel account's email and password, right here (no website tab needed). */
export async function signInWithEmail(email: string, password: string): Promise<Profile> {
  try {
    const { user } = await signInWithEmailAndPassword(auth, email.trim(), password);
    return await recordSession(user);
  } catch (e) {
    throw new Error(emailError(e));
  }
}

/** Sends the "reset your password" email. */
export async function resetPassword(email: string): Promise<void> {
  try {
    await sendPasswordResetEmail(auth, email.trim());
  } catch (e) {
    throw new Error(emailError(e));
  }
}

/** Firebase's error codes, in words. */
function emailError(e: unknown): string {
  const code = (e as { code?: string } | null)?.code ?? "";
  if (["auth/invalid-credential", "auth/wrong-password", "auth/user-not-found", "auth/invalid-login-credentials"].includes(code))
    return "Wrong email or password.";
  if (code === "auth/invalid-email" || code === "auth/missing-email") return "That email doesn't look right.";
  if (code === "auth/missing-password") return "Enter your password.";
  if (code === "auth/too-many-requests") return "Too many tries. Wait a minute and try again.";
  if (code === "auth/user-disabled") return "This account is turned off.";
  if (code === "auth/network-request-failed") return "You seem to be offline.";
  return "Couldn't sign in. Please try again.";
}

/** Records this browser as one of the account's devices, like the desktop apps do on sign-in. */
async function recordSession(user: User): Promise<Profile> {
  const sessionId = crypto.randomUUID().replaceAll("-", "");
  const now = serverTimestamp();
  // Record this browser first, like the desktop apps: if that fails, sign back out so the person can retry.
  try {
    await setDoc(doc(db, "users", user.uid, "sessions", sessionId), {
      platform: "extension",
      deviceName: browserName(),
      appVersion: chrome.runtime.getManifest().version,
      createdAt: now,
      lastSeenAt: now,
    });
  } catch (e) {
    await signOut(auth);
    throw e;
  }
  remember("session", { id: sessionId, checkedAt: Date.now() });
  return toProfile(user);
}

export async function signOutAccount(): Promise<void> {
  await auth.authStateReady(); // a fresh page (the toolbar popup) restores the sign-in from storage first
  const user = auth.currentUser;
  const session = remember<{ id: string }>("session");
  if (user && session) {
    // Offline or already revoked: signing out here is what matters.
    await deleteDoc(doc(db, "users", user.uid, "sessions", session.id)).catch(() => {});
  }
  remember("session", null);
  remember("current", null);
  // The account's shortcuts leave with it; the next sign-in brings the account's own back (sync/extension).
  remember("shortcuts", null);
  remember("shortcuts-expanded", null);
  remember("ext-version", null);
  remember("ext-dirty", null);
  await signOut(auth);
}

/** Signs out if this session was revoked from another device; otherwise records activity. Throttled. */
export async function verifySession(user: Profile): Promise<void> {
  const session = remember<{ id: string; checkedAt: number }>("session");
  if (!session) return; // signed in by an older build: nothing to check
  if (Date.now() - session.checkedAt < verifyEvery) return;
  try {
    const ref = doc(db, "users", user.uid, "sessions", session.id);
    if (!(await getDoc(ref)).exists()) {
      await signOutAccount();
      return;
    }
    await updateDoc(ref, { lastSeenAt: serverTimestamp() });
    remember("session", { id: session.id, checkedAt: Date.now() });
  } catch {
    // Offline or Google is down: stay signed in and check again next time.
  }
}

/** The account's birthday ("YYYY-MM-DD", set on the website's /me), or null when it has none. One read. */
export async function readBirthday(): Promise<string | null> {
  await auth.authStateReady();
  const user = auth.currentUser;
  if (!user) return null;
  const birthday = (await getDoc(doc(db, "users", user.uid))).data()?.birthday;
  return isBirthday(birthday) ? birthday : null;
}

/** The extension's own settings and shortcuts, kept for every browser of the account (users/{uid}/sync/extension). */
export interface ExtensionSync {
  settings: Partial<Settings>;
  shortcuts: Shortcut[];
  updatedAt: number;
}

export async function readExtensionSync(): Promise<ExtensionSync | null> {
  await auth.authStateReady(); // a fresh page (the toolbar popup) restores the sign-in from storage first
  const user = auth.currentUser;
  if (!user) return null;
  const d = (await getDoc(doc(db, "users", user.uid, "sync", "extension"))).data();
  if (!d) return null;
  const updated = d.updatedAt as { toMillis?: () => number } | undefined;
  return {
    settings: typeof d.settings === "object" && d.settings ? (d.settings as Partial<Settings>) : {},
    shortcuts: cleanShortcuts(d.shortcuts),
    updatedAt: updated?.toMillis?.() ?? 0,
  };
}

/** This browser, so it can tell its own doorbell rings from another browser's. */
function browserId(): string {
  let id = remember<string>("browser-id");
  if (!id) id = remember("browser-id", crypto.randomUUID().replaceAll("-", ""))!;
  return id;
}

/**
 * Writes this browser's settings and shortcuts for the account's other browsers (the whole document: last write wins),
 * then rings the doorbell so the ones open right now take them over within a second.
 */
export async function publishExtensionSync(settings: Partial<Settings>, shortcuts: Shortcut[]): Promise<void> {
  await auth.authStateReady(); // a fresh page (the toolbar popup) restores the sign-in from storage first
  const user = auth.currentUser;
  if (!user) throw new Error("Not signed in");
  await setDoc(doc(db, "users", user.uid, "sync", "extension"), {
    settings,
    // Links as {title, url}, folders as {title, items: [{title, url}]}.
    shortcuts: cleanShortcuts(shortcuts).map((s) =>
      isFolder(s) ? { title: s.title, items: s.items.map((l) => ({ title: l.title, url: l.url })) } : { title: s.title, url: s.url },
    ),
    updatedAt: serverTimestamp(),
    device: browserId(),
  });
  const token = await user.getIdToken();
  // A PATCH: it leaves the photo, settings and favorites fields of the doorbell alone. Best effort.
  await fetch(`${realtimeUrl}/pulse/${user.uid}.json?auth=${encodeURIComponent(token)}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ extensionAt: { ".sv": "timestamp" }, extensionDevice: browserId() }),
  }).catch(() => {});
}

/** What any device of the account last set as wallpaper, live while this tab is open. */
export interface DesktopCurrent {
  photo: Photo;
  /** "windows", "linux", "android" or "extension" */
  source: string;
  updatedAt: number;
}

/** The Realtime Database: only the doorbell pulse/{uid} lives there (see website/database.rules.json). */
const realtimeUrl = "https://oriel-gcp-default-rtdb.firebaseio.com";

/**
 * Makes a photo picked here the wallpaper of every device of the account: writes users/{uid}/sync/current (the
 * desktop and Android apps apply it) and rings the doorbell so they do it within a second instead of at their next
 * check. This tab's own listener sees the same document and keeps showing the photo.
 */
export async function publishWallpaper(photo: Photo): Promise<void> {
  await auth.authStateReady(); // a fresh page (the toolbar popup) restores the sign-in from storage first
  const user = auth.currentUser;
  if (!user) throw new Error("Not signed in");
  await setDoc(doc(db, "users", user.uid, "sync", "current"), {
    photoId: photo.id,
    imageUrl: photo.url,
    pageUrl: photo.pageUrl,
    photographer: photo.photographer,
    photographerUrl: photo.photographerUrl,
    ...(photo.description ? { description: photo.description } : {}),
    ...(photo.topic ? { topic: photo.topic } : {}),
    source: "extension",
    updatedAt: serverTimestamp(),
  });
  const token = await user.getIdToken();
  const device = remember<{ id: string }>("session")?.id ?? "";
  // Best effort: without the ring the other devices still pick it up at their next check.
  // A PATCH: it leaves the doorbell's settings fields (rung when shared settings change) alone.
  await fetch(`${realtimeUrl}/pulse/${user.uid}.json?auth=${encodeURIComponent(token)}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ photoId: photo.id, source: "extension", device, at: { ".sv": "timestamp" } }),
  }).catch(() => {});
}

/** What the doorbell connection reports to the page. */
export interface AccountWatch {
  /** Following the account's wallpaper right now ("Same as my devices")? Otherwise sync/current isn't read at all. */
  followsWallpaper(): boolean;
  /** The wallpaper this browser already has (kept locally): a ring for that same photo needs no read. */
  knownPhotoId(): string | undefined;
  onCurrent(current: DesktopCurrent | null): void;
  onError(): void;
  /**
   * The doorbell's "extension settings changed" time: when the connection opens (undefined if never rung) and on each
   * ring. `mine` is a ring from this browser's own upload.
   */
  onExtension(at: number | undefined, mine: boolean): void;
  /** The doorbell's "birthday changed" time (the website rings it): when the connection opens, and on each ring. */
  onBirthday(at: number | undefined): void;
}

/**
 * One live connection per tab to the account's Realtime Database doorbell (pulse/{uid}), rung by every device after it
 * publishes, over a plain EventSource. It serves both the wallpaper (each photo ring reads sync/current once, only while
 * following it) and the extension's shared settings. Both are kept in this browser: sync/current is read only when the
 * doorbell names a photo this browser doesn't have, sync/extension only when its time is newer than this browser's
 * copy. So a new tab usually reads nothing from Firestore.
 */
export function watchAccount(uid: string, watch: AccountWatch): () => void {
  let closed = false;
  let source: EventSource | null = null;
  let retry = 2000;
  let gotFirst = false;

  /** `photoId`: the photo the doorbell names. Already the one here: nothing to read (most new tabs). */
  async function read(photoId?: unknown): Promise<void> {
    if (!watch.followsWallpaper()) return;
    if (typeof photoId === "string" && photoId && photoId === watch.knownPhotoId()) return;
    try {
      const snap = await getDoc(doc(db, "users", uid, "sync", "current"));
      if (!closed) watch.onCurrent(toCurrent(snap.data()));
    } catch {
      if (!closed) watch.onError();
    }
  }

  async function connect(): Promise<void> {
    if (closed || !auth.currentUser) return;
    const token = await auth.currentUser.getIdToken();
    if (closed) return;
    source = new EventSource(`${realtimeUrl}/pulse/${uid}.json?auth=${encodeURIComponent(token)}`);
    // The first event is the whole doorbell: {photoId, ..., extensionAt, extensionDevice} (null if never rung).
    source.addEventListener("put", (e) => {
      gotFirst = true;
      retry = 2000;
      let data: Record<string, unknown> | null | undefined;
      try {
        data = (JSON.parse((e as MessageEvent).data) as { data?: Record<string, unknown> | null }).data;
      } catch {
        // unreadable: read everything once, as if never rung
      }
      void read(data?.photoId);
      watch.onExtension(typeof data?.extensionAt === "number" ? data.extensionAt : undefined, false);
      watch.onBirthday(typeof data?.birthdayAt === "number" ? data.birthdayAt : undefined);
    });
    // Later rings are PATCHes; the fields say what changed (the apps' shared settings and favorites don't concern this page).
    source.addEventListener("patch", (e) => {
      try {
        const data = (JSON.parse((e as MessageEvent).data) as { data?: Record<string, unknown> }).data;
        if (!data) return;
        if ("photoId" in data) void read(data.photoId);
        if (typeof data.extensionAt === "number") watch.onExtension(data.extensionAt, data.extensionDevice === browserId());
        if (typeof data.birthdayAt === "number") watch.onBirthday(data.birthdayAt);
      } catch {
        // not a ring we know
      }
    });
    // The ID token lasts an hour: then the stream ends and a fresh one opens it again.
    source.addEventListener("auth_revoked", () => {
      source?.close();
      void connect();
    });
    source.onerror = () => {
      if (!gotFirst) void read(); // the stream can't open (offline, blocked): show what's there at least
      if (source?.readyState === EventSource.CLOSED) {
        window.setTimeout(() => void connect(), retry);
        retry = Math.min(retry * 2, 60_000);
      }
    };
  }

  void connect();
  return () => {
    closed = true;
    source?.close();
  };
}

function toCurrent(d: Record<string, unknown> | undefined): DesktopCurrent | null {
  if (!d || typeof d.imageUrl !== "string") return null;
  const updated = d.updatedAt as { toMillis?: () => number } | undefined;
  return {
    photo: {
      id: String(d.photoId ?? ""),
      url: d.imageUrl,
      photographer: String(d.photographer ?? ""),
      photographerUrl: withRef(String(d.photographerUrl ?? "")),
      pageUrl: withRef(String(d.pageUrl ?? "")),
      description: String(d.description ?? ""),
      topic: d.topic ? String(d.topic) : undefined,
      color: d.color ? String(d.color) : undefined,
    },
    source: String(d.source ?? ""),
    updatedAt: updated?.toMillis?.() ?? 0,
  };
}

function withRef(link: string): string {
  if (!link.startsWith("https://unsplash.com/")) return "";
  return link.includes("utm_source") ? link : `${link}${link.includes("?") ? "&" : "?"}${unsplashRef}`;
}

function browserName(): string {
  const ua = navigator.userAgent;
  if (ua.includes("Firefox/")) return "Firefox";
  if (ua.includes("Edg/")) return "Edge";
  if (ua.includes("OPR/")) return "Opera";
  if ((navigator as Navigator & { brave?: unknown }).brave) return "Brave";
  return "Chrome";
}
