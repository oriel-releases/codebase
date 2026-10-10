/** Public web config (not secret): access is enforced by Firebase Auth and firestore.rules. Same project as the website and desktop apps. */
export const firebaseConfig = {
  apiKey: "AIzaSyCtUNKnXo3UcKHsG_gtupyf1Tfpo-BzKc8",
  authDomain: "oriel-gcp.firebaseapp.com",
  projectId: "oriel-gcp",
  appId: "1:739177186002:web:183d984eb1ef4bbad2e30b",
};

const productionSite = "https://oriel.akbarjon.uz";

/** The Oriel website, where sign-in happens. For local testing: localStorage.setItem("oriel.site", "http://localhost:3000"). */
export function siteUrl(): string {
  try {
    return localStorage.getItem("oriel.site") ?? productionSite;
  } catch {
    return productionSite;
  }
}

export const unsplashRef = "utm_source=oriel&utm_medium=referral";

export const supportUrl = "https://t.me/oriel_support_bot";
