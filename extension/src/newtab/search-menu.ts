import { escapeHtml } from "../lib/html";
import { menuRows, parseSuggestions, parseWikipedia, suggestUrl, wikipediaUrl, type Row } from "../lib/search-menu";

const typingPauseMs = 150;
/** Wikipedia is a bonus: the list does not wait longer than this for it. */
const wikipediaWaitMs = 700;
const searchIcon = '<circle cx="11" cy="11" r="7" /><path d="m20 20-3.5-3.5" />';

/**
 * Google's suggestions (and a couple of Wikipedia pages with a picture) under the search field, inside the same pill,
 * as you type. Up and Down choose, Enter or a click searches, Escape closes. `sources` says which of the two are turned on in Settings.
 */
export function setUpSearchMenu(form: HTMLFormElement, field: HTMLTextAreaElement, sources: () => { google: boolean; wikipedia: boolean }, pick: (text: string) => void): void {
  const menu = form.querySelector<HTMLElement>("#q-menu")!;
  const list = menu.querySelector<HTMLElement>("ul")!;
  let items: Row[] = [];
  let chosen = -1;
  // What was typed before the arrows started to walk the list; the field gets it back past either end.
  let typedByHand = "";
  // True once an arrow has put a suggestion into the field; until then the field still holds what was typed (hovering a row changes only the highlight).
  let walking = false;
  let timer: number | undefined;
  let asking: AbortController | undefined;
  // The last answer, so the list comes back when the field is focused again with the same text.
  let last = { text: "", rows: [] as Row[] };

  const anySource = () => Object.values(sources()).some(Boolean);

  function show(rows: Row[]): void {
    // Nothing to offer for a text of several lines.
    items = field.value.includes("\n") ? [] : rows;
    chosen = Math.min(chosen, items.length - 1);
    if (items.length) {
      list.innerHTML = items.map(rowHtml).join("");
      menu.dataset.open = "true";
      keepPlace();
    } else close();
    mark();
  }

  /** The pill grows down over the page: its margin takes the growth back so the page keeps its place (to the fraction of a pixel, or the clock would shift by one). */
  function keepPlace(): void {
    form.style.marginBottom = `${-menu.getBoundingClientRect().height}px`;
  }

  function rowHtml(row: Row, index: number): string {
    const icon = row.image
      ? `<img src="${escapeHtml(row.image)}" alt="" />`
      : `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">${searchIcon}</svg>`;
    const about = row.description ? `<small>${escapeHtml(row.description)}</small>` : "";
    return `<li role="option" data-index="${index}"${row.description ? ' class="page"' : ""}>${icon}<span>${escapeHtml(row.text)}${about}</span></li>`;
  }

  function mark(): void {
    list.querySelectorAll("li").forEach((li, index) => li.classList.toggle("on", index === chosen));
  }

  function close(): void {
    clearTimeout(timer);
    asking?.abort();
    chosen = -1;
    walking = false;
    delete menu.dataset.open;
    form.style.marginBottom = "";
  }

  function choose(text: string): void {
    field.value = text;
    field.dispatchEvent(new Event("input"));
    close();
    form.requestSubmit();
  }

  async function ask(text: string): Promise<void> {
    asking?.abort();
    asking = new AbortController();
    try {
      const { signal } = asking;
      const { google, wikipedia } = sources();
      const pages = wikipedia
        ? fetch(wikipediaUrl(text), { signal: AbortSignal.any([signal, AbortSignal.timeout(wikipediaWaitMs)]) })
            .then((reply) => reply.json())
            .then(parseWikipedia)
            .catch((): Row[] => [])
        : Promise.resolve<Row[]>([]);
      const suggestions = google ? parseSuggestions(await (await fetch(suggestUrl(text), { signal })).json()) : [];
      const rows = menuRows(suggestions, await pages);
      last = { text, rows };
      if (field.value.trim() === text) show(rows);
    } catch (error) {
      // Replaced by a newer question: nothing to say. Anything else (offline, no permission yet) is left in the console.
      if ((error as Error).name === "AbortError") return;
      const granted = await chrome.permissions.contains({ origins: [`${new URL(suggestUrl(text)).origin}/*`] }).catch(() => "unknown");
      console.warn(`[Oriel] search suggestions failed (permission granted: ${granted}):`, error);
    }
  }

  // A narrower window can wrap a row, so a resize can change the list's height.
  addEventListener("resize", () => {
    if ("open" in menu.dataset) keepPlace();
  });
  field.addEventListener("focus", () => {
    if (anySource() && last.text && field.value.trim() === last.text) show(last.rows);
  });
  field.addEventListener("input", () => {
    chosen = -1;
    walking = false;
    const text = field.value.trim();
    clearTimeout(timer);
    // The list stays while the next answer is on its way, so it doesn't blink on every key.
    if (!text || !anySource() || text.includes("\n")) return close();
    timer = window.setTimeout(() => void ask(text), typingPauseMs);
  });
  // Leaving the page (another tab, another window) blurs the field too, but the list should be there when you come back.
  form.addEventListener("focusout", () => {
    setTimeout(() => {
      if (document.hasFocus() && !form.contains(document.activeElement)) close();
    });
  });

  field.addEventListener("keydown", (e) => {
    if (!items.length || !("open" in menu.dataset) || e.shiftKey || e.isComposing) return;
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      // One step more than the rows: the extra stop is the typed text itself.
      if (!walking) typedByHand = field.value;
      walking = true;
      const stops = items.length + 1;
      const step = e.key === "ArrowDown" ? 1 : -1;
      chosen = ((chosen + 1 + step + stops) % stops) - 1;
      field.value = chosen === -1 ? typedByHand : items[chosen]!.text;
      field.setSelectionRange(field.value.length, field.value.length);
      mark();
    } else if (e.key === "Enter" && chosen >= 0) {
      e.preventDefault();
      e.stopImmediatePropagation();
      choose(items[chosen]!.text);
    } else if (e.key === "Escape") {
      e.stopPropagation();
      close();
    }
  });

  // The list never takes the focus from the field.
  menu.addEventListener("pointerdown", (e) => e.preventDefault());
  list.addEventListener("click", (e) => {
    const row = (e.target as HTMLElement).closest<HTMLElement>("li");
    if (!row) return;
    // A click goes straight to the search; the field keeps what was typed.
    const { text } = items[Number(row.dataset.index)]!;
    close();
    pick(text);
  });
  list.addEventListener("pointermove", (e) => {
    const row = (e.target as HTMLElement).closest<HTMLElement>("li");
    const index = row ? Number(row.dataset.index) : -1;
    if (index !== chosen) {
      chosen = index;
      mark();
    }
  });
}
