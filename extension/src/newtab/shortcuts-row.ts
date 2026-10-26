import { remember } from "../lib/settings";
import { combine, isFolder, loadShortcuts, makeFolder, maxInFolder, maxShortcuts, normalizeUrl, saveShortcuts, titleFor, updateFolder, type Folder, type Link, type Shortcut } from "../lib/shortcuts";
import { filledCorners, fromIconCache, iconFor, isGenericIcon, useSiteIcon } from "../lib/favicons";
import { escapeHtml } from "../lib/html";
import { createFolderView } from "./folder-view";
import { dots, ease, icon, linkIcon } from "./tiles";
import { createSorter, hint, homeOf, tileAt } from "./drag";

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
  /** Closes the editor and an open folder. */
  closeEditor(): void;
  /** One step back (Esc, the backdrop): the editor to its folder, a folder's question to the folder. False: nothing to. */
  back(): boolean;
}

/** Shown before "Show more". */
const collapsedCount = 6;

const isShortcutTile = (el: Element | null): el is HTMLElement => !!el && el.matches(".sc[data-sc]");

/** A folder's icon: a rounded square with its first four sites in a 2×2 grid (their first letter until the icon loads). */
function folderIcon(folder: Folder): string {
  const minis = folder.items.slice(0, 4).map((l) => {
    const favicon = iconFor(l.url);
    return `<span class="mini"><span class="mini-letter">${escapeHtml((l.title || titleFor(l.url))[0] ?? "")}</span>${
      favicon ? `<img src="${escapeHtml(favicon)}" data-url="${escapeHtml(l.url)}" alt="" />` : ""
    }</span>`;
  });
  return `<span class="sc-ico sc-stack">${minis.join("")}</span>`;
}

type Kind = "link" | "folder" | "pick";

/** Where a link being edited lives: in the row (folder null) or in a folder; index -1 while adding a new one. */
interface Editing {
  folder: number | null;
  index: number;
}

/** The links under the clock: tiles and folders, drag to reorder or to combine, Show more / Show less, the editor. */
export function createShortcutsRow(row: HTMLElement, dialog: HTMLElement, folderCard: HTMLElement, host: ShortcutsHost): ShortcutsRow {
  let shortcuts = loadShortcuts();
  let editing: Editing = { folder: null, index: -1 };
  /** What the row shows now: it is only rebuilt when this changes. */
  let drawnShortcuts = "";
  /** When the row was last drawn: an icon that arrives right after came from the browser's cache and needs no fade. */
  let drawnAt = 0;

  const expanded = () => shortcuts.length > collapsedCount && !!remember<boolean>("shortcuts-expanded");

  /** What the row shows: it is only rebuilt when this changes. */
  const rowKey = () => JSON.stringify([shortcuts, expanded()]);

  /** Under the clock: a round tile per link, a square one per folder, then "Add shortcut" while there's room. */
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
    const tiles = shown.map((s, i) =>
      isFolder(s)
        ? `<div class="sc sc-folder" data-key="${i}" data-sc data-folder>
        <button type="button" class="sc-link" data-open="${i}" aria-label="${escapeHtml(s.title)}, folder of ${s.items.length}">
          ${folderIcon(s)}
          <span class="sc-title">${escapeHtml(s.title)}</span>
        </button>
      </div>`
        : `<div class="sc" data-key="${i}" data-sc>
        <a class="sc-link" href="${escapeHtml(s.url)}" draggable="false">
          ${linkIcon(s.url)}
          <span class="sc-title">${escapeHtml(s.title)}</span>
        </a>
        <button class="sc-edit" data-edit="${i}" aria-label="Edit ${escapeHtml(s.title)}">${dots}</button>
      </div>`,
    );
    if (shortcuts.length > collapsedCount) {
      tiles.push(
        open
          ? `<button class="sc sc-more" data-more data-key="more" aria-expanded="true"><span class="sc-ico">${icon('<path d="M4 14h6v6M20 10h-6V4M14 10l7-7M3 21l7-7"/>')}</span><span class="sc-title">Show less</span></button>`
          : `<button class="sc sc-more" data-more data-key="more" aria-expanded="false"><span class="sc-ico">${icon('<path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7"/>')}</span><span class="sc-title">Show more</span></button>`,
      );
    }
    // Even with a full row: Add still makes folders (a folder takes the place of the shortcuts put in it).
    if (open || shortcuts.length <= collapsedCount) {
      tiles.push(`<button class="sc sc-add" data-add data-key="add"><span class="sc-ico">${icon('<path d="M12 5v14M5 12h14"/>')}</span><span class="sc-title">Add</span></button>`);
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
        const handle = tile.querySelector<HTMLElement>("[data-edit], [data-open]")!;
        if (handle.dataset.edit !== undefined) handle.dataset.edit = String(i);
        else handle.dataset.open = String(i);
      });
      drawnShortcuts = rowKey();
      host.drawn(); // same tiles, new places
    }
    render();
    folders.refresh();
    host.edited();
  }

  // ---- A folder open over the page (folder-view.ts)

  const folders = createFolderView(folderCard, {
    list: () => shortcuts,
    setList: (list) => setShortcuts(list),
    rename: (f, title) => renameFolder(f, title),
    editLink: (f, i) => openEditor({ folder: f, index: i }),
    addLink: (f) => openEditor({ folder: f, index: -1 }),
    modalOpened: () => host.modalOpened(),
    close: () => host.close(),
    tileOf: (f) => row.querySelector<HTMLElement>(`.sc[data-key="${f}"]`),
    watchIcons,
  });

  // ---- Drag a tile: to reorder, or, a link, into a folder. Resting on another tile's edge moves the held one into its
  // place; resting on the middle of its icon makes them a folder (or adds to that folder): the icon swells and glows,
  // and a label over the held tile says what letting go will do. The tile lifts and follows the pointer, the others
  // glide aside, and on release it settles. Our own drag (pointer events), not the browser's faded picture.

  let press: { tile: HTMLElement; id: number; x: number; y: number; dx: number; dy: number; dragging: boolean } | null = null;
  /** The tile the held link would go into, once it has rested on its middle for a moment. */
  let mergeInto: HTMLElement | null = null;
  let mergeCandidate: HTMLElement | null = null;
  let mergeTimer = 0;
  /** Set when a drag ends, so the click the browser fires on release doesn't open the link. */
  let justDragged = false;
  const sorter = createSorter(row, () => [...row.querySelectorAll<HTMLElement>(".sc[data-sc]")]);

  /** What dropping on this tile would do, or "" when it can't take the held link (a full folder). */
  function mergeText(target: HTMLElement): string {
    const s = shortcuts[Number(target.dataset.key)];
    if (!s) return "";
    if (!isFolder(s)) return "Make a folder";
    return s.items.length < maxInFolder ? `Add to “${s.title}”` : "";
  }

  function setMerge(held: HTMLElement, target: HTMLElement | null): void {
    if (target === mergeCandidate) return;
    mergeCandidate = target;
    clearTimeout(mergeTimer);
    mergeInto?.classList.remove("merge-target");
    mergeInto = null;
    hint(held, "");
    if (!target) return;
    const text = mergeText(target);
    if (!text) return;
    // A moment's rest, so passing over a tile on the way somewhere else doesn't start a folder.
    mergeTimer = window.setTimeout(() => {
      mergeInto = target;
      target.classList.add("merge-target");
      hint(held, text);
    }, 280);
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
    const over = tileAt(e.clientX, e.clientY, ".sc[data-sc]", tile);
    // The middle of its icon (a circle a bit larger than the icon), holding a link (folders don't go into folders):
    // into it. Anywhere else on it: its place.
    const ico = over?.querySelector(".sc-ico")?.getBoundingClientRect();
    const middle =
      !!ico && !tile.hasAttribute("data-folder") && Math.hypot(e.clientX - (ico.left + ico.width / 2), e.clientY - (ico.top + ico.height / 2)) < ico.width * 0.62;
    if (middle) {
      sorter.stop();
      setMerge(tile, over);
    } else {
      setMerge(tile, null);
      sorter.rest(tile, over);
    }
    const home = homeOf(tile);
    tile.style.translate = `${e.clientX - press.dx - home.left}px ${e.clientY - press.dy - home.top}px`;
  });

  function endPress(e: PointerEvent): void {
    if (!press || e.pointerId !== press.id) return;
    const { tile, dragging } = press;
    press = null;
    const into = mergeInto;
    setMerge(tile, null);
    sorter.stop();
    tile.querySelector(".sc-hint")?.remove();
    if (!dragging) return;
    justDragged = true;
    setTimeout(() => (justDragged = false), 0);
    row.classList.remove("sorting");
    // The order on screen is the new order; shortcuts hidden behind "Show more" keep their places after these.
    const order = [...row.querySelectorAll<HTMLElement>(".sc[data-sc]")].map((t) => Number(t.dataset.key));
    const reordered = [...order.map((key) => shortcuts[key]), ...shortcuts.filter((_, i) => !order.includes(i))];

    if (into) {
      const from = order.indexOf(Number(tile.dataset.key));
      const onto = order.indexOf(Number(into.dataset.key));
      const result = combine(reordered, from, onto);
      if (result) {
        const wasFolder = isFolder(reordered[onto]!);
        // The link shrinks into the tile it was dropped on, which swells for a moment; then the row redraws.
        const a = tile.getBoundingClientRect();
        const b = into.getBoundingClientRect();
        const [tx = 0, ty = 0] = (tile.style.translate || "0px 0px").split(" ").map(parseFloat);
        const to = `${tx + b.left + b.width / 2 - (a.left + a.width / 2)}px ${ty + b.top + b.height / 2 - (a.top + a.height / 2)}px`;
        tile.classList.remove("held");
        void tile
          .animate([{ translate: `${tx}px ${ty}px`, scale: "1.08", opacity: 1 }, { translate: to, scale: "0.3", opacity: 0 }], {
            duration: 220,
            easing: "cubic-bezier(0.4, 0, 1, 1)",
            fill: "forwards",
          })
          .finished.then(() => {
            setShortcuts(result.list);
            const made = row.querySelector<HTMLElement>(`.sc[data-key="${result.folder}"] .sc-ico`);
            made?.animate([{ scale: "1.18" }, { scale: "1" }], { duration: 380, easing: "cubic-bezier(0.3, 1.6, 0.5, 1)" });
            // A new folder opens at once with its name selected, to be named; adding to one just shows it took it.
            if (!wasFolder) folders.open(result.folder, true);
          });
        return;
      }
    }

    // Settle: from where it was let go into its place in the row.
    const from = tile.style.translate || "0px 0px";
    tile.style.translate = "";
    tile.classList.replace("held", "settling");
    tile.animate([{ translate: from, scale: "1.08" }, { translate: "0 0", scale: "1" }], { duration: 260, easing: ease }).finished.finally(() => {
      tile.classList.remove("settling");
      if (order.every((key, i) => key === i)) return;
      setShortcuts(reordered, true);
    });
  }
  row.addEventListener("pointerup", endPress);
  row.addEventListener("pointercancel", endPress);
  // The release after a drag isn't a click on the link.
  row.addEventListener("click", (e) => justDragged && e.preventDefault(), true);

  /**
   * A favicon fades in once checked and loaded (one the browser already had, most tabs, is simply there). The browser's
   * generic page icon is swapped for the site's own icon; an icon that fails to load is removed (the globe, or a
   * folder's letter, shows instead). Inline <svg> icons fire "load" too: only the favicons are handled.
   */
  function watchIcons(container: HTMLElement): void {
    container.addEventListener(
      "load",
      (e) => {
        if (!(e.target instanceof HTMLImageElement) || !e.target.dataset.url) return;
        const img = e.target;
        const quick = performance.now() - drawnAt < 80;
        const show = () => {
          if (quick) img.classList.add("instant");
          if (filledCorners(img)) img.classList.add("round");
          img.classList.add("loaded");
        };
        const url = img.dataset.url!;
        if (!fromIconCache(img)) return show();
        void isGenericIcon(img).then((generic) => {
          if (!generic) return show();
          img.src = useSiteIcon(url) ?? ""; // loads again: this listener shows it, or "error" removes it
        });
      },
      true,
    );
    container.addEventListener("error", (e) => e.target instanceof HTMLImageElement && e.target.dataset.url && e.target.remove(), true);
  }
  watchIcons(row);
  watchIcons(dialog); // the new folder form's picked links

  row.addEventListener("click", (e) => {
    const target = e.target as HTMLElement;
    const edit = target.closest<HTMLElement>("[data-edit]");
    const folder = target.closest<HTMLElement>("[data-open]");
    if (edit) {
      e.preventDefault();
      openEditor({ folder: null, index: Number(edit.dataset.edit) });
    } else if (folder) folders.open(Number(folder.dataset.open));
    else if (target.closest("[data-add]")) openEditor({ folder: null, index: -1 });
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

  // ---- The add / edit dialog. Editing: a link's name, address and folder. Adding from the row: Shortcut or Folder
  // first (a new folder gets a name and, picked from the row, the links that go in it).

  /**
   * Which form the Add dialog shows. From the row: a new shortcut ("link") or a folder. From a folder: a new shortcut
   * or ones already in the row ("pick"), moved into it.
   */
  let adding: Kind = "link";
  /** The kinds the open Add dialog offers, in the switch's order. */
  let kinds: Kind[] = ["link"];
  /** Links picked for a new folder (their places in the list). */
  let picked = new Set<number>();

  function linkAt(at: Editing): Link | undefined {
    if (at.index < 0) return undefined;
    const s = at.folder === null ? shortcuts[at.index] : shortcuts[at.folder];
    if (!s) return undefined;
    if (at.folder === null) return isFolder(s) ? undefined : s;
    return isFolder(s) ? s.items[at.index] : undefined;
  }

  const folderSvg = icon('<path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/>');
  const linkSvg = icon('<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>');

  function linkForm(at: Editing): string {
    const s = linkAt(at);
    const folderList = shortcuts.map((x, i) => [x, i] as const).filter(([x]) => isFolder(x)) as [Folder, number][];
    const choice = at.folder ?? "";
    // Adding or editing from inside a folder: it stays in that one (dragging it out of the card takes it out), so the
    // place isn't asked.
    const fixed = at.folder !== null;
    // No "New folder" here (that's Add > Folder, or dropping a shortcut on another); with no folder, nothing to choose.
    const where = fixed
      ? `<input type="hidden" name="folder" value="${at.folder}" />`
      : !folderList.length
        ? `<input type="hidden" name="folder" value="" />`
        : `<label class="sc-folder-field">
        <span>${folderSvg}Folder</span>
        <select class="field" name="folder" aria-label="Folder">
          <option value="" ${choice === "" ? "selected" : ""}>None</option>
          ${folderList.map(([f, i]) => `<option value="${i}" ${choice === i ? "selected" : ""}>${escapeHtml(f.title)}</option>`).join("")}
        </select>
      </label>`;
    return `<form class="sc-form" data-form="link" novalidate>
      <input class="field" name="title" placeholder="Name" aria-label="Name" maxlength="40" autocomplete="off" value="${escapeHtml(s?.title ?? "")}" />
      <input class="field" name="url" placeholder="URL" aria-label="URL" maxlength="2000" autocomplete="off" spellcheck="false" value="${escapeHtml(s?.url ?? "")}" />
      ${where}
      <p class="sc-error" role="alert"></p>
      <div class="sc-actions">
        ${s ? `<button type="button" class="btn btn-danger" data-remove>Delete</button>` : ""}
        <span class="flex-1"></span>
        <button type="button" class="btn btn-ghost" data-cancel>Cancel</button>
        <button type="submit" class="btn btn-primary">${s ? "Save" : "Add"}</button>
      </div>
    </form>`;
  }

  /** The links in the row (not folders), with their places in the list. */
  const rowLinks = () => shortcuts.map((x, i) => [x, i] as const).filter(([x]) => !isFolder(x)) as [Link, number][];

  /** How many can be picked: a folder's room when adding to one, else as many as a folder holds. */
  function pickRoom(): number {
    const f = editing.folder === null ? undefined : shortcuts[editing.folder];
    return f && isFolder(f) ? maxInFolder - f.items.length : maxInFolder;
  }

  /** The links in the row as tiles that tick, with the count and All / None over them. */
  function picker(label: string): string {
    const links = rowLinks();
    return `<div class="sc-pick-label"><span>${label}<span class="sc-pick-count">${picked.size ? ` · ${picked.size}` : ""}</span></span><button type="button" class="sc-pick-all" data-pick-all>${picked.size === Math.min(links.length, pickRoom()) ? "None" : "All"}</button></div>
      <div class="sc-pick" role="group" aria-label="${label}">${links
        .map(
          ([l, i]) => `<button type="button" class="sc-pick-item" data-pick="${i}" aria-pressed="${picked.has(i)}">
            ${linkIcon(l.url)}<span class="sc-title">${escapeHtml(l.title)}</span><span class="sc-check">${icon('<path d="M20 6 9 17l-5-5"/>')}</span>
          </button>`,
        )
        .join("")}</div>`;
  }

  const pickActions = (label: string) => `<p class="sc-error" role="alert"></p>
      <div class="sc-actions">
        <span class="flex-1"></span>
        <button type="button" class="btn btn-ghost" data-cancel>Cancel</button>
        <button type="submit" class="btn btn-primary" data-create ${picked.size ? "" : "disabled"}>${label}</button>
      </div>`;

  /** A new folder: its name, and the links in the row to put in it (one at least: a folder is never empty). */
  function folderForm(): string {
    return `<form class="sc-form" data-form="folder" novalidate>
      <input class="field" name="folderTitle" placeholder="Folder name" aria-label="Folder name" maxlength="30" autocomplete="off" />
      ${rowLinks().length ? picker("Put in it") : `<p class="sc-pick-empty">Add a shortcut first, then put it in a folder.</p>`}
      ${pickActions("Create")}
    </form>`;
  }

  /** Adding to a folder from the row: the links picked move into it. */
  function pickForm(): string {
    return `<form class="sc-form" data-form="pick" novalidate>
      ${picker("From the row")}
      ${pickActions("Add")}
    </form>`;
  }

  const formFor = (kind: Kind) => (kind === "folder" ? folderForm() : kind === "pick" ? pickForm() : linkForm(editing));

  function openEditor(at: Editing): void {
    editing = at;
    const s = linkAt(at);
    // Adding from the row: a shortcut or a folder (a folder can't go in a folder). Adding from a folder: a new shortcut
    // or ones already in the row.
    const inFolder = !s && at.folder !== null;
    const choosing = !s && at.folder === null;
    kinds = choosing ? ["link", "folder"] : inFolder ? ["link", "pick"] : ["link"];
    // A full row takes no new shortcut: only a folder made from the shortcuts in it.
    const full = choosing && shortcuts.length >= maxShortcuts;
    const noneInRow = inFolder && rowLinks().length === 0;
    if (!kinds.includes(adding) || noneInRow) adding = "link";
    if (full) adding = "folder";
    picked = new Set();
    // From an open folder, the editor takes its place instead of opening over it.
    folders.suspend();
    dialog.innerHTML = `
    <h2>${s ? "Edit shortcut" : at.folder !== null ? `Add to “${escapeHtml(shortcuts[at.folder]?.title ?? "Folder")}”` : "Add"}</h2>
    ${
      choosing
        ? `<div class="seg sc-kind" style="--n:2;--i:${kinds.indexOf(adding)}" role="group" aria-label="What to add">
        <button type="button" data-kind="link" aria-pressed="${adding === "link"}" ${full ? 'disabled title="The row is full"' : ""}>${linkSvg}Shortcut</button>
        <button type="button" data-kind="folder" aria-pressed="${adding === "folder"}">${folderSvg}Folder</button>
      </div>`
        : inFolder
          ? `<div class="seg sc-kind" style="--n:2;--i:${kinds.indexOf(adding)}" role="group" aria-label="What to add">
        <button type="button" data-kind="link" aria-pressed="${adding === "link"}">${icon('<path d="M12 5v14M5 12h14"/>')}New</button>
        <button type="button" data-kind="pick" aria-pressed="${adding === "pick"}" ${noneInRow ? 'disabled title="No shortcuts in the row"' : ""}>${icon('<path d="M3 12h18M3 6h18M3 18h12"/>')}From the row</button>
      </div>`
          : ""
    }
    <div class="sc-body">${formFor(adding)}</div>`;
    dialog.classList.toggle("wide", adding !== "link");
    dialog.classList.add("open");
    dialog.removeAttribute("inert");
    host.modalOpened();
    focusFirst();
  }

  /** The name first, adding or editing, with the cursor at the end of it. */
  function focusFirst(): void {
    const name = dialog.querySelector<HTMLInputElement>('[name="title"], [name="folderTitle"]');
    name?.focus();
    name?.setSelectionRange(name.value.length, name.value.length);
  }

  /**
   * Shortcut ↔ Folder (or New ↔ From the row): the pill slides, the form below swaps (new rows rising in, from the
   * side picked) while the card eases to its new size.
   */
  function switchKind(kind: Kind): void {
    if (kind === adding || !kinds.includes(kind)) return;
    const forward = kinds.indexOf(kind) > kinds.indexOf(adding);
    adding = kind;
    picked = new Set();
    const seg = dialog.querySelector<HTMLElement>(".sc-kind")!;
    seg.style.setProperty("--i", String(kinds.indexOf(kind)));
    seg.querySelectorAll<HTMLElement>("[data-kind]").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.kind === kind)));
    const body = dialog.querySelector<HTMLElement>(".sc-body")!;
    const height = dialog.offsetHeight;
    const width = dialog.offsetWidth;
    body.innerHTML = formFor(kind);
    dialog.classList.toggle("wide", kind !== "link");
    focusFirst();
    if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    dialog.style.overflow = "hidden"; // only while it eases
    dialog
      .animate(
        [
          { height: `${height}px`, width: `${width}px` },
          { height: `${dialog.offsetHeight}px`, width: `${dialog.offsetWidth}px` },
        ],
        { duration: 300, easing: ease },
      )
      .finished.finally(() => (dialog.style.overflow = ""));
    // The fields rise in; Cancel and Add / Create at the bottom stay put.
    [...body.firstElementChild!.children].filter((el) => !el.matches(".sc-actions")).forEach((el, i) =>
      (el as HTMLElement).animate([{ opacity: 0, translate: forward ? "14px 0" : "-14px 0" }, { opacity: 1, translate: "0 0" }], {
        duration: 280,
        delay: i * 35,
        easing: ease,
        fill: "backwards",
      }),
    );
  }

  /** The count, All / None and Create follow what's picked (a folder needs one shortcut at least). */
  function pickedChanged(): void {
    const total = dialog.querySelectorAll("[data-pick]").length;
    dialog.querySelector(".sc-pick-count")!.textContent = picked.size ? ` · ${picked.size}` : "";
    dialog.querySelector("[data-pick-all]")!.textContent = picked.size === Math.min(total, pickRoom()) ? "None" : "All";
    dialog.querySelector<HTMLButtonElement>("[data-create]")!.disabled = picked.size === 0;
  }

  /** Closes just the editor: back to the open folder if it came from one, else everything. */
  function leaveEditor(): void {
    dialog.classList.remove("open");
    dialog.setAttribute("inert", "");
    if (folders.isOpen) folders.resume();
    else host.close();
  }

  function closeEditor(): void {
    dialog.classList.remove("open");
    dialog.setAttribute("inert", "");
    folders.close();
  }

  /**
   * Puts `link` where the editor says, replacing the one being edited. The new place is filled first and the old link
   * removed after, by identity, so no index shifts in between.
   */
  function place(link: Link, dest: number | null): Shortcut[] | string {
    const old = linkAt(editing);
    const sameFolder = editing.index >= 0 && dest === editing.folder;
    if (sameFolder) {
      if (editing.folder === null) return shortcuts.map((s, i) => (i === editing.index ? link : s));
      return shortcuts.map((s, i) => (i === editing.folder && isFolder(s) ? { ...s, items: s.items.map((l, k) => (k === editing.index ? link : l)) } : s));
    }
    // Where a link going to the row goes: in its old place, after its old folder, or at the end.
    const anchor = editing.index < 0 ? (editing.folder ?? shortcuts.length - 1) + 1 : editing.folder === null ? editing.index : editing.folder + 1;
    let list: Shortcut[] = [...shortcuts];
    const leavesRow = editing.folder === null && editing.index >= 0;
    if (dest === null) {
      if (list.length - (leavesRow ? 1 : 0) >= maxShortcuts) return "The row is full. Put it in a folder.";
      list.splice(anchor, 0, link);
    } else {
      const target = list[dest];
      if (!target || !isFolder(target)) return "That folder is gone.";
      if (target.items.length >= maxInFolder) return `A folder holds ${maxInFolder} shortcuts. That one is full.`;
      list[dest] = { ...target, items: [...target.items, link] };
    }
    if (old) {
      list = list.flatMap((s) => {
        if (s === old) return [];
        if (!isFolder(s) || !s.items.includes(old)) return [s];
        return [{ ...s, items: s.items.filter((l) => l !== old) }];
      });
    }
    return list;
  }

  /** After saving: a folder made just now opens with its name selected; otherwise back to where the editor came from. */
  function afterSave(made: number, rename: boolean): void {
    if (made < 0) return leaveEditor();
    dialog.classList.remove("open");
    dialog.setAttribute("inert", "");
    folders.open(made, rename);
  }

  dialog.addEventListener("submit", (e) => {
    e.preventDefault();
    const form = e.target as HTMLFormElement;
    const error = form.querySelector(".sc-error")!;
    if (form.dataset.form === "pick") {
      // Into the open folder, in the row's order; they leave the row. The folder may move up (links before it left).
      const target = shortcuts[editing.folder!];
      if (!target || !isFolder(target) || !picked.size) return;
      const links = [...picked]
        .sort((a, b) => a - b)
        .map((i) => shortcuts[i])
        .filter((x): x is Link => !!x && !isFolder(x))
        .slice(0, pickRoom());
      const moved: Folder = { ...target, items: [...target.items, ...links] };
      const list = shortcuts.map((x) => (x === target ? moved : x)).filter((x) => !links.includes(x as Link));
      folders.follow(list.indexOf(moved));
      setShortcuts(list);
      return leaveEditor();
    }
    if (form.dataset.form === "folder") {
      const title = (form.elements.namedItem("folderTitle") as HTMLInputElement).value;
      const result = makeFolder(shortcuts, title, [...picked]);
      if (!result) return; // nothing picked: Create isn't enabled then
      setShortcuts(result.list);
      return afterSave(result.folder, !title.trim());
    }
    const url = normalizeUrl((form.elements.namedItem("url") as HTMLInputElement).value);
    if (!url) {
      error.textContent = "Enter a web address, like example.com, or a file path";
      return;
    }
    const title = (form.elements.namedItem("title") as HTMLInputElement).value.trim() || titleFor(url);
    const choice = (form.elements.namedItem("folder") as HTMLSelectElement | HTMLInputElement).value;
    const result = place({ title, url }, choice === "" ? null : Number(choice));
    if (typeof result === "string") {
      error.textContent = result;
      return;
    }
    setShortcuts(result);
    leaveEditor();
  });
  dialog.addEventListener("click", (e) => {
    const target = e.target as HTMLElement;
    const kind = target.closest<HTMLElement>("[data-kind]");
    if (kind) return switchKind(kind.dataset.kind as Kind);
    const pick = target.closest<HTMLElement>("[data-pick]");
    if (pick) {
      const i = Number(pick.dataset.pick);
      if (picked.has(i)) picked.delete(i);
      else if (picked.size < pickRoom()) picked.add(i);
      pick.setAttribute("aria-pressed", String(picked.has(i)));
      return pickedChanged();
    }
    if (target.closest("[data-pick-all]")) {
      const all = [...dialog.querySelectorAll<HTMLElement>("[data-pick]")].map((el) => Number(el.dataset.pick));
      const room = Math.min(all.length, pickRoom());
      picked = picked.size === room ? new Set() : new Set(all.slice(0, room));
      dialog.querySelectorAll<HTMLElement>("[data-pick]").forEach((el) => el.setAttribute("aria-pressed", String(picked.has(Number(el.dataset.pick)))));
      return pickedChanged();
    }
    if (target.closest("[data-cancel]")) return leaveEditor();
    if (!target.closest("[data-remove]")) return;
    const old = linkAt(editing);
    if (!old) return;
    setShortcuts(
      shortcuts.flatMap((s) => {
        if (s === old) return [];
        if (!isFolder(s) || !s.items.includes(old)) return [s];
        return [{ ...s, items: s.items.filter((l) => l !== old) }];
      }),
    );
    leaveEditor();
  });

  /** A folder's new name: saved, and written into its tile in place (the row isn't redrawn, so nothing flickers). */
  function renameFolder(f: number, title: string): void {
    shortcuts = updateFolder(shortcuts, f, (x) => ({ ...x, title }));
    saveShortcuts(shortcuts);
    const tile = row.querySelector<HTMLElement>(`.sc[data-key="${f}"]`);
    if (tile) {
      tile.querySelector(".sc-title")!.textContent = title;
      const folder = shortcuts[f];
      if (folder && isFolder(folder)) tile.querySelector("[data-open]")?.setAttribute("aria-label", `${title}, folder of ${folder.items.length}`);
      drawnShortcuts = rowKey();
    } else render();
    host.edited();
  }

  return {
    get items() {
      return shortcuts;
    },
    render,
    reload() {
      shortcuts = loadShortcuts();
      render();
      folders.refresh();
    },
    replace(items) {
      shortcuts = items;
      saveShortcuts(items, true);
      render();
      folders.refresh();
    },
    closeEditor,
    back() {
      if (dialog.classList.contains("open")) {
        if (!folders.isOpen) return false; // from the row: closing it is all there is
        leaveEditor();
        return true;
      }
      return folders.back();
    },
  };
}
