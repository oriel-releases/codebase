/** The browser extension (code in extension/). Store links stay null until each listing is approved; the page shows "Coming soon" for those. */
export const extension = {
  name: "Oriel for browsers",
  version: "1.0.1",
  stores: [
    { id: "chrome", name: "Chrome Web Store", browsers: "Chrome, Brave, Opera and Vivaldi", url: "https://chromewebstore.google.com/detail/oriel/jnpehcopmbingbgjedpbdeamkompbbol" as string | null },
    { id: "edge", name: "Microsoft Edge Add-ons", browsers: "Edge", url: "https://microsoftedge.microsoft.com/addons/detail/oriel/kndnegimfojlhmfjnfgccdphmbmdginh" as string | null },
    { id: "firefox", name: "Firefox Add-ons", browsers: "Firefox", url: "https://addons.mozilla.org/addon/oriel" as string | null },
  ],
} as const;

export type ExtensionStore = (typeof extension.stores)[number];
