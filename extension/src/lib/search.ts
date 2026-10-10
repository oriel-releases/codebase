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
