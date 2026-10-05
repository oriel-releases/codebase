import { GoogleAuthProvider, signInWithPopup } from "firebase/auth";
import { doc, runTransaction, serverTimestamp } from "firebase/firestore";
import { firebaseAuth, firestore } from "@/lib/firebase";

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

/** Opens Google sign-in, then creates or refreshes the account's `users/{uid}` document. */
export async function signInWithGoogle(): Promise<SignedIn> {
  const auth = await firebaseAuth();
  const result = await signInWithPopup(auth, new GoogleAuthProvider());
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

  return { email: user.email, googleIdToken, signedInAt: Date.now() };
}

/** The link that opens the Oriel app (registered by the Windows app) and signs it in. */
export function appLink(result: SignedIn): string {
  return `oriel://auth?token=${encodeURIComponent(result.googleIdToken)}`;
}
