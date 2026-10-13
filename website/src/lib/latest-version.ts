/** The apps that have their own release. */
export type AppPlatform = "windows" | "linux" | "android";

/** Firestore document holding the newest released version of one app: `application/{platform}`, field `version`. */
function documentUrl(platform: AppPlatform): string {
  return `https://firestore.googleapis.com/v1/projects/oriel-gcp/databases/(default)/documents/application/${platform}`;
}

/** The latest version string, or null if it can't be read (offline, rules, missing document). */
export async function fetchLatestVersion(platform: AppPlatform, signal?: AbortSignal): Promise<string | null> {
  try {
    const response = await fetch(documentUrl(platform), { signal, cache: "no-store" });
    if (!response.ok) return null;
    const data: { fields?: { version?: { stringValue?: string } } } = await response.json();
    const version = data.fields?.version?.stringValue?.trim();
    return version || null;
  } catch {
    return null;
  }
}
