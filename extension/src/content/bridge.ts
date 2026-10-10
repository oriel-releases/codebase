// Runs only on the Oriel website's sign-in page. After Google sign-in, the page posts the credential here
// and this hands it to the extension. A page can't talk to an extension directly in Firefox, so this works
// the same way in both browsers: page -> content script -> extension storage -> new tab page.
//
// It stays dependency-free so it builds to one small file.

interface Handoff {
  token: string;
  at: number;
}

window.addEventListener("message", (event: MessageEvent) => {
  // Only the page itself (not an iframe or another window) may hand over a credential.
  if (event.source !== window || event.origin !== location.origin) return;
  const data = event.data as { source?: string; type?: string; token?: unknown } | null;
  if (data?.source !== "oriel-web" || data.type !== "extension-auth" || typeof data.token !== "string" || !data.token) return;

  const handoff: Handoff = { token: data.token, at: Date.now() };
  chrome.storage.local.set({ handoff }).then(
    () => window.postMessage({ source: "oriel-extension", type: "connected" }, location.origin),
    () => window.postMessage({ source: "oriel-extension", type: "failed" }, location.origin),
  );
});
