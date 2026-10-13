import {
  EmailAuthProvider,
  GoogleAuthProvider,
  linkWithCredential,
  linkWithPopup,
  reauthenticateWithCredential,
  reauthenticateWithPopup,
  sendEmailVerification,
  unlink,
  updatePassword,
  updateProfile,
  type User,
} from "firebase/auth";
import { googleProvider, hasGoogle, hasPassword, saveProfile } from "@/lib/sign-in";

/** How the account can sign in, for the account page. */
export type Methods = {
  /** The Google account connected to it (its email), or null. */
  google: { email: string } | null;
  password: boolean;
  /** Only meaningful with a password: Google accounts are verified by Google. */
  emailVerified: boolean;
};

export function methodsOf(user: User): Methods {
  const google = user.providerData.find((p) => p.providerId === GoogleAuthProvider.PROVIDER_ID);
  return { google: google ? { email: google.email ?? "" } : null, password: hasPassword(user), emailVerified: user.emailVerified };
}

/** New passwords: Firebase's own minimum is 6; 8 is the floor worth having. */
export const minPasswordLength = 8;

/** Thrown when Firebase wants a fresh sign-in before a sensitive change; the page asks the person to confirm. */
export const needsRecentLogin = (e: unknown) => (e as { code?: string }).code === "auth/requires-recent-login";

/** Adds a password to an account that signs in with Google, so it can sign in with its email too. */
export async function addPassword(user: User, password: string): Promise<void> {
  if (!user.email) throw new Error("This account has no email address.");
  await linkWithCredential(user, EmailAuthProvider.credential(user.email, password));
}

/** Changes the password, confirming the current one first (so it never needs the "recent sign-in" detour). */
export async function changePassword(user: User, current: string, next: string): Promise<void> {
  if (!user.email) throw new Error("This account has no email address.");
  await reauthenticateWithCredential(user, EmailAuthProvider.credential(user.email, current));
  await updatePassword(user, next);
}

/**
 * Connects a Google account. An account made with a password has no name or photo yet: it takes Google's, and the
 * profile document follows.
 */
export async function connectGoogle(user: User): Promise<void> {
  const result = await linkWithPopup(user, googleProvider({ hint: user.email ?? undefined }));
  const google = result.user.providerData.find((p) => p.providerId === GoogleAuthProvider.PROVIDER_ID);
  const patch: { displayName?: string; photoURL?: string } = {};
  if (!result.user.displayName && google?.displayName) patch.displayName = google.displayName;
  if (!result.user.photoURL && google?.photoURL) patch.photoURL = google.photoURL;
  if (Object.keys(patch).length) await updateProfile(result.user, patch);
  await saveProfile(result.user).catch(() => {});
}

/** Removes Google or the password. Never the last one: the account would be impossible to sign in to. */
export async function disconnect(user: User, method: "google" | "password"): Promise<void> {
  if (!(hasGoogle(user) && hasPassword(user))) throw new Error("Keep at least one way to sign in.");
  await unlink(user, method === "google" ? GoogleAuthProvider.PROVIDER_ID : EmailAuthProvider.PROVIDER_ID);
}

/** Confirms who it is before a sensitive change: with Google if the account has it, otherwise with the password. */
export async function confirmIdentity(user: User, password?: string): Promise<void> {
  if (password !== undefined) {
    if (!user.email) throw new Error("This account has no email address.");
    await reauthenticateWithCredential(user, EmailAuthProvider.credential(user.email, password));
  } else {
    await reauthenticateWithPopup(user, googleProvider({ hint: user.email ?? undefined }));
  }
}

export async function sendVerification(user: User): Promise<void> {
  await sendEmailVerification(user, { url: `${window.location.origin}/me` });
}

/** Words for a failed change on the account page, or undefined when the person just closed Google's window. */
export function methodError(e: unknown): string | undefined {
  switch ((e as { code?: string }).code) {
    case "auth/popup-closed-by-user":
    case "auth/cancelled-popup-request":
      return undefined;
    case "auth/popup-blocked":
      return "Your browser blocked Google's window. Allow pop-ups for this site and try again.";
    case "auth/credential-already-in-use":
    case "auth/email-already-in-use":
      return "That Google account already belongs to another Oriel account.";
    case "auth/provider-already-linked":
      return "Already connected.";
    case "auth/invalid-credential":
    case "auth/wrong-password":
    case "auth/invalid-login-credentials":
      return "That password isn't right.";
    case "auth/user-mismatch":
      return "That's a different Google account from the one connected here.";
    case "auth/weak-password":
    case "auth/password-does-not-meet-requirements":
      return `Pick a longer password: at least ${minPasswordLength} characters.`;
    case "auth/too-many-requests":
      return "Too many tries. Wait a few minutes and try again.";
    case "auth/network-request-failed":
      return "Couldn't reach Google. Check your connection.";
    case "auth/operation-not-allowed":
      return "Passwords aren't available right now. Please try again later.";
    default:
      return "Something went wrong. Please try again.";
  }
}
