// Everything that talks to Firebase. It is imported lazily (see main.ts) so a new tab for someone
// who never linked an account doesn't load the SDK at all.
import { initializeApp } from "firebase/app";
import { GoogleAuthProvider, getAuth, onAuthStateChanged, signInWithCredential, signOut, type User } from "firebase/auth/web-extension";
import { deleteDoc, doc, getDoc, getFirestore, onSnapshot, serverTimestamp, setDoc, updateDoc } from "firebase/firestore";
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

/** What the desktop app last set as wallpaper, live while this tab is open. */
export interface DesktopCurrent {
  photo: Photo;
  /** "windows" or "linux" */
  source: string;
  updatedAt: number;
}

export function watchDesktopWallpaper(uid: string, callback: (current: DesktopCurrent | null) => void, onError: () => void): () => void {
  return onSnapshot(
    doc(db, "users", uid, "sync", "current"),
    (snap) => {
      const d = snap.data();
      if (!d || typeof d.imageUrl !== "string") return callback(null);
      callback({
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
        updatedAt: snap.data()?.updatedAt?.toMillis?.() ?? 0,
      });
    },
    onError,
  );
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
