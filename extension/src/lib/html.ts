const escapes: Record<string, string> = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };

/** Text made safe to put into HTML, as content or as an attribute value. */
export const escapeHtml = (s: string) => s.replace(/[&<>"']/g, (c) => escapes[c]);
