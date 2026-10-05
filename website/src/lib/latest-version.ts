/** Firestore document holding the newest released app version: `application/version`, field `version`. */
const documentUrl =
  "https://firestore.googleapis.com/v1/projects/oriel-gcp/databases/(default)/documents/application/version";

/** The latest version string, or null if it can't be read (offline, rules, missing document). */
export async function fetchLatestVersion(signal?: AbortSignal): Promise<string | null> {
  try {
    const response = await fetch(documentUrl, { signal, cache: "no-store" });
    if (!response.ok) return null;
    const data: { fields?: { version?: { stringValue?: string } } } = await response.json();
    const version = data.fields?.version?.stringValue?.trim();
    return version || null;
  } catch {
    return null;
  }
}
