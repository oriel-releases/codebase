import { getApp, getApps, initializeApp } from "firebase/app";
import { getAuth } from "firebase/auth";
import { getFirestore } from "firebase/firestore";

/** Public web config (not secret): access is enforced by Firebase Auth and firestore.rules. */
const config = {
  apiKey: "AIzaSyCtUNKnXo3UcKHsG_gtupyf1Tfpo-BzKc8",
  authDomain: "oriel-gcp.firebaseapp.com",
  projectId: "oriel-gcp",
  appId: "1:739177186002:web:183d984eb1ef4bbad2e30b",
};

export function firebaseApp() {
  return getApps().length ? getApp() : initializeApp(config);
}

/**
 * The sign-in is remembered in this browser (Firebase's default), so /me can show your account without asking again.
 * Signing in still always yields a fresh Google credential for the app or extension.
 */
export async function firebaseAuth() {
  return getAuth(firebaseApp());
}

export function firestore() {
  return getFirestore(firebaseApp());
}
