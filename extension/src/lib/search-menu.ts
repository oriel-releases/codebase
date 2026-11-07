/** What the list under the search field offers: Google's suggestions, and a few Wikipedia pages with a picture. */
const suggestHost = "https://suggestqueries.google.com";
const wikipediaApi = "https://en.wikipedia.org/w/api.php";
const maxSuggestions = 5;
const maxPages = 2;
const thumbnailSize = 96;

/** One row: `text` is what goes into the field and gets searched; a page also has a title, a short line and a picture. */
export interface Row {
  text: string;
  description?: string;
  image?: string;
}

/** The address browsers themselves ask for suggestions (no key). */
export function suggestUrl(text: string): string {
  return `${suggestHost}/complete/search?client=firefox&q=${encodeURIComponent(text)}`;
}

/** The suggestions in Google's answer: [query, [suggestion, ...], ...]. Anything else is no suggestions. */
export function parseSuggestions(answer: unknown): string[] {
  if (!Array.isArray(answer) || !Array.isArray(answer[1])) return [];
  return answer[1].filter((item): item is string => typeof item === "string").slice(0, maxSuggestions);
}

/** Wikipedia's open search: pages starting with the text, each with its short description and a small picture. */
export function wikipediaUrl(text: string): string {
  const params = new URLSearchParams({
    action: "query",
    format: "json",
    origin: "*",
    generator: "prefixsearch",
    gpssearch: text,
    gpslimit: String(maxPages + 3),
    prop: "pageimages|description",
    piprop: "thumbnail",
    pithumbsize: String(thumbnailSize),
    pilimit: String(maxPages + 3),
    redirects: "1",
  });
  return `${wikipediaApi}?${params}`;
}

interface WikipediaPage {
  title?: unknown;
  index?: unknown;
  description?: unknown;
  thumbnail?: { source?: unknown };
}

/** The pages of Wikipedia's answer in the order it ranks them, without pages that only point to others. */
export function parseWikipedia(answer: unknown): Row[] {
  const pages = (answer as { query?: { pages?: Record<string, WikipediaPage> } } | null)?.query?.pages;
  if (!pages) return [];
  return Object.values(pages)
    .filter((page) => typeof page.title === "string" && typeof page.index === "number")
    .sort((a, b) => (a.index as number) - (b.index as number))
    .map((page): Row => ({
      text: page.title as string,
      description: typeof page.description === "string" ? page.description : undefined,
      image: typeof page.thumbnail?.source === "string" ? page.thumbnail.source : undefined,
    }))
    .filter((row) => !/disambiguation|referred to by the same term/i.test(row.description ?? ""))
    .slice(0, maxPages);
}

/** Google's suggestions first, then the Wikipedia pages that aren't already one of them. */
export function menuRows(suggestions: string[], pages: Row[]): Row[] {
  const rows: Row[] = suggestions.map((text) => ({ text }));
  for (const page of pages) {
    if (!rows.some((row) => row.text.toLowerCase() === page.text.toLowerCase())) rows.push(page);
  }
  return rows;
}
