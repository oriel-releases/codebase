/**
 * Loads the Firebase part of the extension (it's only needed once there's an account). An Oriel page left open
 * across an update of the extension still points at the old file, which no longer exists: then the page reloads
 * itself once, picking up the new files, instead of failing with "Failed to fetch dynamically imported module".
 */
export async function loadAccount(): Promise<typeof import("./account")> {
  try {
    const account = await import("./account");
    sessionStorage.removeItem("oriel.reloaded-for-update");
    return account;
  } catch (e) {
    if (!sessionStorage.getItem("oriel.reloaded-for-update")) {
      sessionStorage.setItem("oriel.reloaded-for-update", "1");
      location.reload();
      await new Promise(() => {}); // the page is going away
    }
    throw e;
  }
}
