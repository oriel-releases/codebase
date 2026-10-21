import { remember } from "../lib/settings";
import { loadShortcuts, maxShortcuts, normalizeUrl, saveShortcuts, titleFor, type Shortcut } from "../lib/shortcuts";
import { filledCorners, fromIconCache, iconFor, isGenericIcon, useSiteIcon } from "../lib/favicons";
import { escapeHtml } from "../lib/html";

/** What the shortcut row needs from the page around it. */
export interface ShortcutsHost {
  /** Settings > Shortcuts: whether the row shows at all. */
  visible(): boolean;
  /** The list was changed here (added, edited, removed, reordered): redraw the panel and upload it soon. */
  edited(): void;
  /** The editor opened: dim the page behind it. */
  modalOpened(): void;
  /** Closes the editor together with everything else open (it calls {@link ShortcutsRow.closeEditor}). */
  close(): void;
  /** The row was drawn or changed size: its text tone (black or white, one for all tiles) follows the photo behind it. */
  drawn(): void;
}

export interface ShortcutsRow {
  readonly items: Shortcut[];
  /** Draws the row; a no-op when nothing it shows changed. */
  render(): void;
  /** Reads the list again (changed in another tab, or erased by signing out). */
  reload(): void;
  /** Takes over a list that came from the account. */
  replace(items: Shortcut[]): void;
  closeEditor(): void;
}

const icon = (path: string) =>
  `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${path}</svg>`;

/** Shown before "Show more". */
const collapsedCount = 6;

/** Stands in for a site whose icon can't be had (hidden while one is shown). */
const globe = `<span class="sc-globe">${icon('<circle cx="12" cy="12" r="10"/><path d="M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20M2 12h20"/>')}</span>`;

const ease = "cubic-bezier(0.2, 0.8, 0.2, 1)";

const isShortcutTile = (el: Element | null): el is HTMLElement => !!el && el.matches(".sc[data-sc]");

/** The links under the clock: tiles, drag to reorder, Show more / Show less, and the add / edit dialog. */
export function createShortcutsRow(row: HTMLElement, dialog: HTMLElement, host: ShortcutsHost): ShortcutsRow {
  let shortcuts = loadShortcuts();
  /** The shortcut being edited, or -1 while adding one. */
  let editing = -1;
  /** What the row shows now: it is only rebuilt when this changes. */
  let drawnShortcuts = "";
  /** When the row was last drawn: an icon that arrives right after came from the browser's cache and needs no fade. */
  let drawnAt = 0;

  const expanded = () => shortcuts.length > collapsedCount && !!remember<boolean>("shortcuts-expanded");

  /** What the row shows: it is only rebuilt when this changes. */
  const rowKey = () => JSON.stringify([shortcuts, expanded()]);

  /** Under the clock: a round tile per link, then "Add shortcut" while there's room. */
  function render(): void {
    row.hidden = !host.visible();
    if (row.hidden) {
      drawnShortcuts = "";
      return;
    }
    const open = expanded();
    const key = rowKey();
    if (key === drawnShortcuts) return; // a setting changed, not the shortcuts: leave the row as it is
    drawnShortcuts = key;
    // More than collapsedCount: the rest (and Add) wait behind "Show more", like Chrome's own new tab.
    const shown = shortcuts.length > collapsedCount && !open ? shortcuts.slice(0, collapsedCount) : shortcuts;
    // data-key: the same tile across redraws, so Show more / Show less can move it smoothly (toggleMore).
    const tiles = shown.map((s, i) => {
      const favicon = iconFor(s.url);
      return `<div class="sc" data-key="${i}" data-sc>
        <a class="sc-link" href="${escapeHtml(s.url)}" draggable="false">
          <span class="sc-ico">${favicon ? `<img src="${escapeHtml(favicon)}" alt="" />` : ""}${globe}</span>
          <span class="sc-title">${escapeHtml(s.title)}</span>
        </a>
        <button class="sc-edit" data-edit="${i}" aria-label="Edit ${escapeHtml(s.title)}">${icon('<circle cx="12" cy="5" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="12" cy="19" r="1"/>')}</button>
      </div>`;
    });
    if (shortcuts.length > collapsedCount) {
      tiles.push(
        open
          ? `<button class="sc sc-more" data-more data-key="more" aria-expanded="true"><span class="sc-ico">${icon('<path d="M4 14h6v6M20 10h-6V4M14 10l7-7M3 21l7-7"/>')}</span><span class="sc-title">Show less</span></button>`
          : `<button class="sc sc-more" data-more data-key="more" aria-expanded="false"><span class="sc-ico">${icon('<path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7"/>')}</span><span class="sc-title">Show more</span></button>`,
      );
    }
    if (shortcuts.length < maxShortcuts && (open || shortcuts.length <= collapsedCount)) {
      tiles.push(`<button class="sc sc-add" data-add data-key="add"><span class="sc-ico">${icon('<path d="M12 5v14M5 12h14"/>')}</span><span class="sc-title">Add shortcut</span></button>`);
    }
    row.innerHTML = tiles.join("");
    drawnAt = performance.now();
    host.drawn();
  }

  /** `inPlace`: the tiles on screen already show this order (a drag just ended), so they're renumbered, not rebuilt. */
  function setShortcuts(items: Shortcut[], inPlace = false): void {
    shortcuts = items;
    saveShortcuts(items);
    if (inPlace) {
      row.querySelectorAll<HTMLElement>(".sc[data-sc]").forEach((tile, i) => {
        tile.dataset.key = String(i);
        tile.querySelector<HTMLElement>("[data-edit]")!.dataset.edit = String(i);
      });
      drawnShortcuts = rowKey();
      host.drawn(); // same tiles, new places
    }
    render();
    host.edited();
  }

  // ---- Drag a shortcut to reorder: the tile lifts and follows the pointer, the others glide aside, and on release it
  // settles into its new place. Our own drag (pointer events), not the browser's, which only drags a faded picture.

  let press: { tile: HTMLElement; id: number; x: number; y: number; dx: number; dy: number; dragging: boolean } | null = null;
  /** Set when a drag ends, so the click the browser fires on release doesn't open the link. */
  let justDragged = false;

  /** Moves tiles in the page and lets every tile that moved (but the one held) glide from where it was (FLIP). */
  function glide(change: () => void, held?: HTMLElement): void {
    const tiles = [...row.querySelectorAll<HTMLElement>(".sc")].filter((t) => t !== held);
    const before = new Map(tiles.map((t) => [t, t.getBoundingClientRect()]));
    change();
    for (const t of tiles) {
      const a = before.get(t)!;
      const b = t.getBoundingClientRect();
      const dx = a.left - b.left;
      const dy = a.top - b.top;
      if (dx || dy) t.animate([{ translate: `${dx}px ${dy}px` }, { translate: "0 0" }], { duration: 240, easing: ease });
    }
  }

  /** Where the tile sits in the row, ignoring the offset it's drawn at while held. */
  function homeOf(tile: HTMLElement): DOMRect {
    const offset = tile.style.translate;
    tile.style.translate = "";
    const box = tile.getBoundingClientRect();
    tile.style.translate = offset;
    return box;
  }

  row.addEventListener("pointerdown", (e) => {
    if (e.button !== 0 || (e.target as HTMLElement).closest(".sc-edit")) return;
    const tile = (e.target as HTMLElement).closest(".sc");
    if (!isShortcutTile(tile) || row.querySelectorAll(".sc[data-sc]").length < 2) return; // nothing to reorder
    const box = tile.getBoundingClientRect();
    press = { tile, id: e.pointerId, x: e.clientX, y: e.clientY, dx: e.clientX - box.left, dy: e.clientY - box.top, dragging: false };
  });

  row.addEventListener("pointermove", (e) => {
    if (!press || e.pointerId !== press.id) return;
    const { tile } = press;
    if (!press.dragging) {
      if (Math.hypot(e.clientX - press.x, e.clientY - press.y) < 6) return; // a click, so far
      press.dragging = true;
      tile.setPointerCapture(e.pointerId);
      tile.classList.add("held");
      row.classList.add("sorting");
    }
    // The tile under the pointer (the held one is skipped): move the held tile before or after it.
    const over = document
      .elementsFromPoint(e.clientX, e.clientY)
      .map((el) => el.closest(".sc"))
      .find((el) => isShortcutTile(el) && el !== tile) as HTMLElement | undefined;
    if (over) {
      const box = over.getBoundingClientRect();
      const target = e.clientX > box.left + box.width / 2 ? over.nextElementSibling : over;
      if (target !== tile && target !== tile.nextElementSibling) glide(() => row.insertBefore(tile, target), tile);
    }
    const home = homeOf(tile);
    tile.style.translate = `${e.clientX - press.dx - home.left}px ${e.clientY - press.dy - home.top}px`;
  });

  function endPress(e: PointerEvent): void {
    if (!press || e.pointerId !== press.id) return;
    const { tile, dragging } = press;
    press = null;
    if (!dragging) return;
    justDragged = true;
    setTimeout(() => (justDragged = false), 0);
    // Settle: from where it was let go into its place in the row.
    const from = tile.style.translate || "0px 0px";
    tile.style.translate = "";
    row.classList.remove("sorting");
    tile.classList.replace("held", "settling");
    tile.animate([{ translate: from, scale: "1.08" }, { translate: "0 0", scale: "1" }], { duration: 260, easing: ease }).finished.finally(() => {
      tile.classList.remove("settling");
      // The order on screen is the new order; shortcuts hidden behind "Show more" keep their places after these.
      const order = [...row.querySelectorAll<HTMLElement>(".sc[data-sc]")].map((t) => Number(t.dataset.key));
      if (order.every((key, i) => key === i)) return;
      setShortcuts([...order.map((key) => shortcuts[key]), ...shortcuts.filter((_, i) => !order.includes(i))], true);
    });
  }
  row.addEventListener("pointerup", endPress);
  row.addEventListener("pointercancel", endPress);
  // The release after a drag isn't a click on the link.
  row.addEventListener("click", (e) => justDragged && e.preventDefault(), true);

  // A favicon fades in once checked and loaded (one the browser already had, most tabs, is simply there). The browser's
  // generic page icon is swapped for the site's own icon; an icon that fails to load is removed, showing a globe.
  // Inline <svg> icons fire "load" too: only the favicons are handled.
  row.addEventListener(
    "load",
    (e) => {
      if (!(e.target instanceof HTMLImageElement)) return;
      const img = e.target;
      const quick = performance.now() - drawnAt < 80;
      const show = () => {
        if (quick) img.classList.add("instant");
        if (filledCorners(img)) img.classList.add("round");
        img.classList.add("loaded");
      };
      const url = img.closest(".sc")?.querySelector<HTMLAnchorElement>(".sc-link")?.getAttribute("href");
      if (!url || !fromIconCache(img)) return show();
      void isGenericIcon(img).then((generic) => {
        if (!generic) return show();
        img.src = useSiteIcon(url) ?? ""; // loads again: this listener shows it, or "error" removes it
      });
    },
    true,
  );
  row.addEventListener("error", (e) => e.target instanceof HTMLImageElement && e.target.remove(), true);
  row.addEventListener("click", (e) => {
    const target = e.target as HTMLElement;
    const edit = target.closest<HTMLElement>("[data-edit]");
    if (edit) {
      e.preventDefault();
      openEditor(Number(edit.dataset.edit));
    } else if (target.closest("[data-add]")) openEditor(-1);
    else if (target.closest("[data-more]")) void toggleMore();
  });

  let toggling = false;

  /**
   * Show more / Show less (this browser only, not synced), animated: the tiles already there glide to their new places,
   * the row grows or shrinks smoothly (so the clock above moves with it instead of jumping), and the extra tiles fade in
   * one after another, sharpening from a soft blur. Closing fades the extra tiles out first, then the row closes.
   */
  async function toggleMore(): Promise<void> {
    if (toggling) return;
    toggling = true;
    const opening = !remember<boolean>("shortcuts-expanded");
    const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
    try {
      if (!opening && !reduced) {
        const leaving = [...row.querySelectorAll<HTMLElement>("[data-key]")].filter(
          (el) => el.dataset.key === "add" || Number(el.dataset.key) >= collapsedCount,
        );
        await Promise.all(
          leaving.map((el, i) =>
            el.animate([{ opacity: 1, scale: 1, filter: "blur(0)" }, { opacity: 0, scale: 0.92, filter: "blur(6px)" }], {
              duration: 180,
              delay: (leaving.length - 1 - i) * 15,
              easing: "ease-in",
              fill: "forwards",
            }).finished,
          ),
        );
      }
      // FLIP: where everything is now, then redraw, then play each tile from its old place to its new one.
      const before = new Map([...row.querySelectorAll<HTMLElement>("[data-key]")].map((el) => [el.dataset.key!, el.getBoundingClientRect()]));
      const height = row.offsetHeight;
      remember("shortcuts-expanded", opening);
      render();
      if (reduced) return;
      row.classList.add("resizing");
      row
        .animate([{ height: `${height}px` }, { height: `${row.offsetHeight}px` }], { duration: 420, easing: ease })
        .finished.finally(() => row.classList.remove("resizing"));
      let fresh = 0;
      for (const el of row.querySelectorAll<HTMLElement>("[data-key]")) {
        const old = before.get(el.dataset.key!);
        if (old) {
          const now = el.getBoundingClientRect();
          const dx = old.left - now.left;
          const dy = old.top - now.top;
          if (dx || dy) el.animate([{ translate: `${dx}px ${dy}px` }, { translate: "0 0" }], { duration: 420, easing: ease });
        } else {
          el.animate([{ opacity: 0, scale: 0.92, filter: "blur(6px)" }, { opacity: 1, scale: 1, filter: "blur(0)" }], {
            duration: 360,
            delay: 60 + fresh++ * 35,
            easing: ease,
            fill: "backwards",
          });
        }
      }
    } finally {
      toggling = false;
    }
  }

  // ---- The add / edit dialog

  function openEditor(index: number): void {
    editing = index;
    const s = shortcuts[index];
    dialog.innerHTML = `
    <h2>${s ? "Edit shortcut" : "Add shortcut"}</h2>
    <form class="sc-form" novalidate>
      <input class="field" name="title" placeholder="Name" aria-label="Name" maxlength="40" autocomplete="off" value="${escapeHtml(s?.title ?? "")}" />
      <input class="field" name="url" placeholder="URL" aria-label="URL" maxlength="2000" autocomplete="off" spellcheck="false" value="${escapeHtml(s?.url ?? "")}" />
      <p class="sc-error" role="alert"></p>
      <div class="sc-actions">
        ${s ? `<button type="button" class="btn btn-danger" data-remove>Delete</button>` : ""}
        <span class="flex-1"></span>
        <button type="submit" class="btn btn-primary">${s ? "Save" : "Add"}</button>
      </div>
    </form>`;
    dialog.classList.add("open");
    dialog.removeAttribute("inert");
    host.modalOpened();
    // The name first, adding or editing, with the cursor at the end of it.
    const name = dialog.querySelector<HTMLInputElement>('[name="title"]')!;
    name.focus();
    name.setSelectionRange(name.value.length, name.value.length);
  }

  function closeEditor(): void {
    dialog.classList.remove("open");
    dialog.setAttribute("inert", "");
  }

  dialog.addEventListener("submit", (e) => {
    e.preventDefault();
    const form = e.target as HTMLFormElement;
    const url = normalizeUrl((form.elements.namedItem("url") as HTMLInputElement).value);
    if (!url) {
      form.querySelector(".sc-error")!.textContent = "Enter a web address, like example.com";
      return;
    }
    const title = (form.elements.namedItem("title") as HTMLInputElement).value.trim() || titleFor(url);
    const items = [...shortcuts];
    if (editing >= 0) items[editing] = { title, url };
    else items.push({ title, url });
    setShortcuts(items);
    host.close();
  });
  dialog.addEventListener("click", (e) => {
    if ((e.target as HTMLElement).closest("[data-remove]")) {
      setShortcuts(shortcuts.filter((_, i) => i !== editing));
      host.close();
    }
  });

  return {
    get items() {
      return shortcuts;
    },
    render,
    reload() {
      shortcuts = loadShortcuts();
      render();
    },
    replace(items) {
      shortcuts = items;
      saveShortcuts(items, true);
      render();
    },
    closeEditor,
  };
}
