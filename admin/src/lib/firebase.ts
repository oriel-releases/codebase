import { getApp, getApps, initializeApp } from "firebase/app";
import { getAuth, GoogleAuthProvider, signInWithPopup, signOut as firebaseSignOut } from "firebase/auth";
import { getFirestore } from "firebase/firestore";
import { clearCache } from "@/lib/cache";

/** Public web config (not secret), the same as the website's: access is enforced by firestore.rules (isAdmin). */
const config = {
  apiKey: "AIzaSyCtUNKnXo3UcKHsG_gtupyf1Tfpo-BzKc8",
  authDomain: "oriel-gcp.firebaseapp.com",
  projectId: "oriel-gcp",
  appId: "1:739177186002:web:183d984eb1ef4bbad2e30b",
};

/**
 * The one account let in. This only decides what the page shows; the real lock is `isAdmin()` in
 * website/firestore.rules, which checks the same address on Google's side. Keep the two the same.
 */
export const adminEmail = "akbarjonjorayev001@gmail.com";

export function firebaseApp() {
  return getApps().length ? getApp() : initializeApp(config);
}

export const auth = () => getAuth(firebaseApp());
export const db = () => getFirestore(firebaseApp());

export async function signIn() {
  const provider = new GoogleAuthProvider();
  provider.setCustomParameters({ login_hint: adminEmail });
  await signInWithPopup(auth(), provider);
}

/** Signs out and forgets everything read (the cache holds every account's email). */
export async function signOut() {
  clearCache();
  await firebaseSignOut(auth());
}
