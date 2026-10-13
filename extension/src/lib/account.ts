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
import { deleteDoc, doc, getDoc, getFirestore, serverTimestamp, setDoc, updateDoc } from "firebase/firestore/lite";
import { firebaseConfig, unsplashRef } from "./config";
import type { Photo } from "./photo";
import { remember } from "./settings";

const app = initializeApp(firebaseConfig);
const auth = getAuth(app);
const db = getFirestore(app);

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
  const user = auth.currentUser;
  const session = remember<{ id: string }>("session");
  if (user && session) {
    // Offline or already revoked: signing out here is what matters.
    await deleteDoc(doc(db, "users", user.uid, "sessions", session.id)).catch(() => {});
  }
  remember("session", null);
  remember("current", null);
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

/**
 * Follows the account's current wallpaper while the tab is open: the Realtime Database doorbell (pulse/{uid}, rung by
 * every device after it publishes) arrives over a plain EventSource, and each ring reads sync/current once. The first
 * event is the doorbell as it is now, so opening a tab costs one read.
 */
export function watchDesktopWallpaper(uid: string, callback: (current: DesktopCurrent | null) => void, onError: () => void): () => void {
  let closed = false;
  let source: EventSource | null = null;
  let retry = 2000;
  let gotFirst = false;

  async function read(): Promise<void> {
    try {
      const snap = await getDoc(doc(db, "users", uid, "sync", "current"));
      if (!closed) callback(toCurrent(snap.data()));
    } catch {
      if (!closed) onError();
    }
  }

  async function connect(): Promise<void> {
    if (closed || !auth.currentUser) return;
    const token = await auth.currentUser.getIdToken();
    if (closed) return;
    source = new EventSource(`${realtimeUrl}/pulse/${uid}.json?auth=${encodeURIComponent(token)}`);
    // The first event ("put") is the doorbell as it is: read once. Later rings are PATCHes; only a photo ring (not one
    // for the devices' shared settings, which this page doesn't use) means a new wallpaper.
    source.addEventListener("put", () => {
      gotFirst = true;
      retry = 2000;
      void read();
    });
    source.addEventListener("patch", (e) => {
      try {
        const data = (JSON.parse((e as MessageEvent).data) as { data?: Record<string, unknown> }).data;
        if (data && "photoId" in data) void read();
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
