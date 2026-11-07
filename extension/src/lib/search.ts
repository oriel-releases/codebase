/** What the search box text means. Pure: the page decides how to act on it. */
export type SearchTarget = { kind: "address"; url: string } | { kind: "query"; text: string };

/** An address (with or without https://) opens directly; anything else is a web search. Empty text is nothing. */
export function parseSearch(input: string): SearchTarget | null {
  const text = input.trim();
  if (!text) return null;
  const looksLikeAddress = !/\s/.test(text) && /^(https?:\/\/|[\w-]+(\.[\w-]+)+(\/|$))/i.test(text);
  if (looksLikeAddress) return { kind: "address", url: /^https?:\/\//i.test(text) ? text : `https://${text}` };
  return { kind: "query", text };
}

/** Google's own search box sends a new line as CR LF (%0D%0A); a lone LF is not read as a line break there. */
export function asGoogleLines(text: string): string {
  return text.replace(/\r?\n/g, "\r\n");
}

/** Google's own results address. Used for text with several lines, which the browser's search API would flatten to one. */
export function googleSearchUrl(text: string): string {
  return `https://www.google.com/search?q=${encodeURIComponent(asGoogleLines(text))}`;
}
