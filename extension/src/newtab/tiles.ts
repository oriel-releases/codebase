import { iconFor } from "../lib/favicons";
import { escapeHtml } from "../lib/html";
import { isFileLink } from "../lib/shortcuts";

// Small pieces shared by the shortcut row and an open folder (folder-view.ts).

export const icon = (path: string) =>
  `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${path}</svg>`;

/** Stands in for a site whose icon can't be had (hidden while one is shown). */
export const globe = `<span class="sc-globe">${icon('<circle cx="12" cy="12" r="10"/><path d="M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20M2 12h20"/>')}</span>`;
/** A file on this computer (a file:// shortcut): no site, so no favicon. */
const file = `<span class="sc-globe">${icon('<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/>')}</span>`;
export const dots = icon('<circle cx="12" cy="5" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="12" cy="19" r="1"/>');

export const ease = "cubic-bezier(0.2, 0.8, 0.2, 1)";

/** A link's round icon: its favicon (faded in when loaded), the globe behind it until then. A file shows a page. */
export function linkIcon(url: string): string {
  if (isFileLink(url)) return `<span class="sc-ico">${file}</span>`;
  const favicon = iconFor(url);
  return `<span class="sc-ico">${favicon ? `<img src="${escapeHtml(favicon)}" data-url="${escapeHtml(url)}" alt="" />` : ""}${globe}</span>`;
}

