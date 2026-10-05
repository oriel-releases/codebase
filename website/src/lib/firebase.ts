import { getApp, getApps, initializeApp } from "firebase/app";
import { getAuth, inMemoryPersistence, setPersistence } from "firebase/auth";
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
 * The site only signs in to hand the account to the app, so nothing is remembered in the browser:
 * every visit to /login starts fresh and yields a new Google credential for the app.
 */
export async function firebaseAuth() {
  const auth = getAuth(firebaseApp());
  await setPersistence(auth, inMemoryPersistence);
  return auth;
}

export function firestore() {
  return getFirestore(firebaseApp());
}
