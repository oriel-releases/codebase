import { pendingShareQuery } from "@/lib/pending-share";
import { GoogleAuthProvider, signInWithPopup } from "firebase/auth";
import { doc, runTransaction, serverTimestamp } from "firebase/firestore";
import { firebaseAuth, firestore } from "@/lib/firebase";
import { ensureWebSession } from "@/lib/sessions";

export type SignedIn = {
  email: string;
  /** Google ID token for the desktop app to exchange for its own Firebase session. */
  googleIdToken: string;
  signedInAt: number;
};

/** Google ID tokens last an hour; the app can't use an older one. */
const tokenLifetimeMs = 50 * 60 * 1000;

export function isFresh(result: SignedIn): boolean {
  return Date.now() - result.signedInAt < tokenLifetimeMs;
}

type SignInOptions = {
  /** Pre-selects this account in Google's window (the one already signed in here). */
  hint?: string;
  /** Asks Google to show the account chooser instead. */
  chooser?: boolean;
};

/** Opens Google sign-in, then creates or refreshes the account's `users/{uid}` document. */
export async function signInWithGoogle({ hint, chooser }: SignInOptions = {}): Promise<SignedIn> {
  const auth = await firebaseAuth();
  const provider = new GoogleAuthProvider();
  if (hint) provider.setCustomParameters({ login_hint: hint });
  else if (chooser) provider.setCustomParameters({ prompt: "select_account" });
  const result = await signInWithPopup(auth, provider);
  const googleIdToken = GoogleAuthProvider.credentialFromResult(result)?.idToken;
  const { user } = result;
  if (!googleIdToken || !user.email) throw new Error("Google didn't return an account.");

  const ref = doc(firestore(), "users", user.uid);
  await runTransaction(firestore(), async (tx) => {
    const exists = (await tx.get(ref)).exists();
    const profile = { email: user.email, displayName: user.displayName ?? "", photoURL: user.photoURL ?? "" };
    if (exists) tx.update(ref, profile);
    else tx.set(ref, { ...profile, createdAt: serverTimestamp() });
  });

  // Remember this browser as a session, so /me can list it ("This device") and it can be ended from anywhere.
  await ensureWebSession(user.uid);

  return { email: user.email, googleIdToken, signedInAt: Date.now() };
}

/**
 * The link that opens the Oriel app (registered by the Windows and Linux apps) and signs it in. A wallpaper someone
 * shared before Oriel was installed comes along (see pending-share.ts).
 */
export function appLink(result: SignedIn): string {
  return `oriel://auth?token=${encodeURIComponent(result.googleIdToken)}${pendingShareQuery()}`;
}

/**
 * Passes the Google credential to the Oriel browser extension. Its content script (running on this page) listens
 * for the message and answers once it has stored it. Resolves false if no extension answers.
 */
export function handToExtension(result: SignedIn): Promise<boolean> {
  return new Promise((resolve) => {
    const finish = (ok: boolean) => {
      window.removeEventListener("message", onMessage);
      clearTimeout(timer);
      resolve(ok);
    };
    const onMessage = (event: MessageEvent) => {
      if (event.source !== window || event.origin !== window.location.origin) return;
      if (event.data?.source !== "oriel-extension") return;
      if (event.data.type === "connected") finish(true);
      else if (event.data.type === "failed") finish(false);
    };
    const timer = setTimeout(() => finish(false), 4000);
    window.addEventListener("message", onMessage);
    window.postMessage({ source: "oriel-web", type: "extension-auth", token: result.googleIdToken }, window.location.origin);
  });
}
