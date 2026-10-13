import { pendingShareQuery } from "@/lib/pending-share";
import {
  createUserWithEmailAndPassword,
  GoogleAuthProvider,
  sendEmailVerification,
  sendPasswordResetEmail,
  signInWithEmailAndPassword,
  signInWithPopup,
  updateProfile,
  type User,
} from "firebase/auth";
import { doc, runTransaction, serverTimestamp } from "firebase/firestore";
import { firebaseAuth, firestore } from "@/lib/firebase";
import { ensureWebSession } from "@/lib/sessions";

/**
 * What the website hands to the desktop app so it can sign in as the same account:
 * - `google`: a Google ID token (an hour). Every app version understands it; the extension needs it.
 * - `session`: the Firebase refresh token of this sign-in, for accounts without Google (email and password). The app
 *   turns it into its own session. Needs Oriel 1.0.8 (Windows) / 1.0.3 (Linux) or later.
 */
export type Handover = { kind: "google"; idToken: string } | { kind: "session"; refreshToken: string };

export type SignedIn = {
  email: string;
  handover: Handover;
  /** The account can also sign in with Google (so the extension can be connected with a Google sign-in). */
  hasGoogle: boolean;
  signedInAt: number;
};

/** Google ID tokens last an hour; the app can't use an older one. */
const tokenLifetimeMs = 50 * 60 * 1000;

export function isFresh(result: SignedIn): boolean {
  return result.handover.kind !== "google" || Date.now() - result.signedInAt < tokenLifetimeMs;
}

export const hasGoogle = (user: User) => user.providerData.some((p) => p.providerId === GoogleAuthProvider.PROVIDER_ID);
export const hasPassword = (user: User) => user.providerData.some((p) => p.providerId === "password");

type SignInOptions = {
  /** Pre-selects this account in Google's window (the one already signed in here). */
  hint?: string;
  /** Asks Google to show the account chooser instead. */
  chooser?: boolean;
};

/** Opens Google sign-in, then creates or refreshes the account's `users/{uid}` document. */
export async function signInWithGoogle({ hint, chooser }: SignInOptions = {}): Promise<SignedIn> {
  const auth = await firebaseAuth();
  const result = await signInWithPopup(auth, googleProvider({ hint, chooser }));
  const idToken = GoogleAuthProvider.credentialFromResult(result)?.idToken;
  if (!idToken || !result.user.email) throw new Error("Google didn't return an account.");
  await finishSignIn(result.user);
  return { email: result.user.email, handover: { kind: "google", idToken }, hasGoogle: true, signedInAt: Date.now() };
}

/** Signs in with an email and password. */
export async function signInWithPassword(email: string, password: string): Promise<SignedIn> {
  const { user } = await signInWithEmailAndPassword(await firebaseAuth(), email.trim(), password);
  await finishSignIn(user);
  return sessionResult(user);
}

/** Creates an account with an email and password, then sends the address a verification link (best effort). */
export async function createAccount(name: string, email: string, password: string): Promise<SignedIn> {
  const { user } = await createUserWithEmailAndPassword(await firebaseAuth(), email.trim(), password);
  // The name before the profile document, so the document (and friends, who see the first name) get it.
  if (name.trim()) await updateProfile(user, { displayName: name.trim() });
  await finishSignIn(user);
  void sendEmailVerification(user, { url: `${window.location.origin}/me` }).catch(() => {});
  return sessionResult(user);
}

/**
 * Sends a password reset link. Firebase doesn't say whether the address has an account (so nobody can probe for
 * accounts here), so neither does the page.
 */
export async function sendPasswordReset(email: string): Promise<void> {
  await sendPasswordResetEmail(await firebaseAuth(), email.trim(), { url: `${window.location.origin}/login?method=email` });
}

/**
 * Hands the account already signed in on this browser to the app, without asking again. Only for accounts without
 * Google: those with Google go through Google (see LoginCard), which every app version understands.
 */
export async function continueAsCurrent(): Promise<SignedIn> {
  const user = (await firebaseAuth()).currentUser;
  if (!user) throw new Error("Nobody is signed in here.");
  await finishSignIn(user);
  return sessionResult(user);
}

function sessionResult(user: User): SignedIn {
  return {
    email: user.email ?? "",
    handover: { kind: "session", refreshToken: user.refreshToken },
    hasGoogle: hasGoogle(user),
    signedInAt: Date.now(),
  };
}

export function googleProvider({ hint, chooser }: SignInOptions = {}) {
  const provider = new GoogleAuthProvider();
  if (hint) provider.setCustomParameters({ login_hint: hint });
  else if (chooser) provider.setCustomParameters({ prompt: "select_account" });
  return provider;
}

/** After any sign-in: create or refresh `users/{uid}`, and remember this browser as a session. */
async function finishSignIn(user: User): Promise<void> {
  await saveProfile(user);
  // Remember this browser as a session, so /me can list it ("This device") and it can be ended from anywhere.
  await ensureWebSession(user.uid);
}

/** Creates or refreshes the account's `users/{uid}` document from the Firebase user. */
export async function saveProfile(user: User): Promise<void> {
  if (!user.email) return;
  const ref = doc(firestore(), "users", user.uid);
  await runTransaction(firestore(), async (tx) => {
    const exists = (await tx.get(ref)).exists();
    const profile = { email: user.email, displayName: user.displayName ?? "", photoURL: user.photoURL ?? "" };
    if (exists) tx.update(ref, profile);
    else tx.set(ref, { ...profile, createdAt: serverTimestamp() });
  });
}

/** Words for a failed sign-in, or undefined when the person just closed Google's window. */
export function signInError(e: unknown): string | undefined {
  switch ((e as { code?: string }).code) {
    case "auth/popup-closed-by-user":
    case "auth/cancelled-popup-request":
      return undefined;
    case "auth/popup-blocked":
      return "Your browser blocked Google's window. Allow pop-ups for this site and try again.";
    case "auth/invalid-credential":
    case "auth/wrong-password":
    case "auth/user-not-found":
    case "auth/invalid-login-credentials":
      return "That email and password don't match. If you joined with Google, continue with Google instead.";
    case "auth/invalid-email":
      return "That doesn't look like an email address.";
    case "auth/missing-password":
      return "Enter your password.";
    case "auth/email-already-in-use":
      return "There's already an Oriel account with this email. Sign in instead (with Google, if that's how you joined).";
    case "auth/weak-password":
    case "auth/password-does-not-meet-requirements":
      return "Pick a longer password: at least 8 characters.";
    case "auth/account-exists-with-different-credential":
      return "This email already has an Oriel account with a password. Sign in with it, then connect Google on your account page.";
    case "auth/user-disabled":
      return "This account has been turned off. Contact support if that's a mistake.";
    case "auth/too-many-requests":
      return "Too many tries. Wait a few minutes, or reset your password.";
    case "auth/network-request-failed":
      return "Couldn't reach Google. Check your connection.";
    case "auth/operation-not-allowed":
      return "Signing in with email isn't available right now. Continue with Google instead.";
    default:
      return "Couldn't sign you in. Please try again.";
  }
}

/**
 * The link that opens the Oriel app (registered by the Windows and Linux apps) and signs it in. A wallpaper someone
 * shared before Oriel was installed comes along (see pending-share.ts).
 */
export function appLink(result: SignedIn): string {
  const { handover } = result;
  const credential =
    handover.kind === "google" ? `token=${encodeURIComponent(handover.idToken)}` : `refresh=${encodeURIComponent(handover.refreshToken)}`;
  return `oriel://auth?${credential}${pendingShareQuery()}`;
}

/**
 * Passes the Google credential to the Oriel browser extension. Its content script (running on this page) listens
 * for the message and answers once it has stored it. Resolves false if no extension answers. Google sign-ins only:
 * the extension has no way to use a password sign-in yet.
 */
export function handToExtension(result: SignedIn & { handover: { kind: "google" } }): Promise<boolean> {
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
    window.postMessage({ source: "oriel-web", type: "extension-auth", token: result.handover.idToken }, window.location.origin);
  });
}
