/**
 * The toolbar icon. On other pages it shows popup.html under the icon (the manifest's default). An Oriel new tab turns
 * the popup off for itself (main.ts), so a click lands here instead and the page opens its own settings dialog.
 * The browser drops that per-tab override once the tab goes elsewhere, and the popup is back.
 */
chrome.action.onClicked.addListener((tab) => {
  if (tab.id !== undefined) void chrome.tabs.sendMessage(tab.id, { type: "oriel:open-settings" }).catch(() => {});
});
