/**
 * Keeps what was read from Firestore, so reloading the tab or coming back to a page costs no reads (Spark: 50,000 a
 * day). In memory for this tab, and in localStorage for `ttl`, so a reload within it reads nothing. Refresh skips it;
 * signing out clears it (it holds every account's email). Not cookies: those hold 4 KB and ride along every request.
 */

const prefix = "oriel-admin:";
export const TTL = 10 * 60_000;

const memory = new Map<string, { at: number; value: unknown }>();
const pending = new Map<string, Promise<unknown>>();

export function cached<T>(key: string, load: () => Promise<T>, refresh = false, ttl = TTL): Promise<T> {
  if (!refresh) {
    const hit = memory.get(key) ?? fromStorage(key);
    if (hit && Date.now() - hit.at < ttl) {
      memory.set(key, hit);
      return Promise.resolve(hit.value as T);
    }
    const inFlight = pending.get(key);
    if (inFlight) return inFlight as Promise<T>;
  }
  const promise = load()
    .then((value) => {
      const entry = { at: Date.now(), value };
      memory.set(key, entry);
      toStorage(key, entry);
      return value;
    })
    .finally(() => pending.delete(key));
  pending.set(key, promise);
  return promise;
}

/** When `key` was read from Firestore, if it's kept. */
export function readAt(key: string): Date | null {
  const at = memory.get(key)?.at;
  return at ? new Date(at) : null;
}

export function clearCache() {
  memory.clear();
  try {
    // Data only: the page's remembered choices (`pref:`, see use-remembered.ts) hold no one's details and stay.
    for (const k of Object.keys(localStorage)) if (k.startsWith(prefix) && !k.startsWith(`${prefix}pref:`)) localStorage.removeItem(k);
  } catch {
    // Storage blocked: only the memory copy existed.
  }
}

// Dates survive the trip through JSON as {"$d": ms}.
function toStorage(key: string, entry: { at: number; value: unknown }) {
  try {
    localStorage.setItem(
      prefix + key,
      JSON.stringify(entry, function (this: Record<string, unknown>, k, v) {
        const raw = this[k];
        return raw instanceof Date ? { $d: raw.getTime() } : v;
      }),
    );
  } catch {
    // Full or blocked: the memory copy still works for this tab.
  }
}

function fromStorage(key: string): { at: number; value: unknown } | undefined {
  try {
    const text = localStorage.getItem(prefix + key);
    if (!text) return undefined;
    return JSON.parse(text, (_, v) => (v && typeof v === "object" && typeof v.$d === "number" && Object.keys(v).length === 1 ? new Date(v.$d) : v));
  } catch {
    return undefined;
  }
}
