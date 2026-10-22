import { deleteFolder, isFolder, maxInFolder, maxShortcuts, takeOut, ungroup, updateFolder, type Folder, type Shortcut } from "../lib/shortcuts";
import { escapeHtml } from "../lib/html";
import { dots, ease, icon, linkIcon } from "./tiles";
import { createSorter, homeOf, tileAt } from "./drag";

/** What an open folder needs from the shortcut row. */
export interface FolderViewHost {
  list(): Shortcut[];
  /** Saves a changed list and redraws the row (and this folder). */
  setList(list: Shortcut[]): void;
  /** Saves a new name and writes it into the folder's tile in place (nothing is redrawn). */
  rename(folder: number, title: string): void;
  editLink(folder: number, index: number): void;
  addLink(folder: number): void;
  /** Opened: dim the page behind (the shared backdrop). */
  modalOpened(): void;
  /** Closes everything open (the folder, the editor, the backdrop). */
  close(): void;
  /** The folder's tile in the row, to open from and close into. */
  tileOf(folder: number): HTMLElement | null;
  /** Favicons fade in when loaded, a failed one is removed (shortcuts-row.ts). */
  watchIcons(container: HTMLElement): void;
}

const trash = '<path d="M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>';

/**
 * A folder open over the page: it grows out of its tile into a glass card with the folder's name (rename it right
 * there) and its links. Links open on click, reorder by dragging, and leave the folder when dragged out of the card
 * (back into the row). Ungroup and Delete turn the card into the question itself (no window over a window): ungroup
 * puts the links back in the row; delete removes the folder, and with a switch, its links too.
 */
export function createFolderView(card: HTMLElement, host: FolderViewHost) {
  let index = -1;
  /** The card asks instead of showing the folder: "Delete …?" or "Ungroup …?" (no window over a window). */
  let asking: "delete" | "ungroup" | null = null;
  let alsoLinks = false;
  /** What the card shows now: it is only redrawn when this changes (a rename touches the title only). */
  let drawnKey = "";
  /** Bumped by every open and close, so a close still animating doesn't hide a folder opened meanwhile. */
  let turn = 0;
  host.watchIcons(card);

  const reduced = () => matchMedia("(prefers-reduced-motion: reduce)").matches;

  const folder = (): Folder | null => {
    const s = host.list()[index];
    return s && isFolder(s) ? s : null;
  };

  const keyOf = (f: Folder | null) => JSON.stringify([index, f?.items, asking, alsoLinks]);

  function draw(): void {
    const f = folder();
    if (!f) return;
    // Its tile in the row stays empty while it's open (the card is that tile, grown): again after a redraw of the row.
    host.tileOf(index)?.classList.add("fv-source");
    const key = keyOf(f);
    if (key === drawnKey) {
      // Same links: at most the name changed (here, or in another tab). Not while it's being typed in.
      const title = card.querySelector<HTMLInputElement>(".fv-title");
      if (title && document.activeElement !== title && title.value !== f.title) title.value = f.title;
      return;
    }
    // Folder ↔ a question: the card eases to its new height and the new rows rise in one after another.
    const swapping = drawnKey !== "" && JSON.parse(drawnKey)[2] !== asking;
    const height = card.offsetHeight;
    drawnKey = key;
    card.innerHTML = asking === "delete" ? deleteHtml(f) : asking === "ungroup" ? ungroupHtml(f) : folderHtml(f);
    if (!swapping || reduced()) return;
    card.style.overflow = "hidden"; // only while it eases: a link dragged out of the card must show outside it
    card
      .animate([{ height: `${height}px` }, { height: `${card.offsetHeight}px` }], { duration: 320, easing: ease })
      .finished.finally(() => (card.style.overflow = ""));
    [...(card.querySelector(".fv-confirm") ?? card).children].forEach((el, i) => fadeIn(el as HTMLElement, i));
  }

  const gridIcon = '<rect width="7" height="7" x="3" y="3" rx="1"/><rect width="7" height="7" x="14" y="3" rx="1"/><rect width="7" height="7" x="14" y="14" rx="1"/><rect width="7" height="7" x="3" y="14" rx="1"/>';

  function folderHtml(f: Folder): string {
    const tiles = f.items.map(
      (l, i) => `<div class="sc fv-sc" data-i="${i}">
        <a class="sc-link" href="${escapeHtml(l.url)}" draggable="false">
          ${linkIcon(l.url)}
          <span class="sc-title">${escapeHtml(l.title)}</span>
        </a>
        <button class="sc-edit" data-fv-edit="${i}" aria-label="Edit ${escapeHtml(l.title)}">${dots}</button>
      </div>`,
    );
    if (f.items.length < maxInFolder) {
      tiles.push(`<button class="sc sc-add" data-fv-add><span class="sc-ico">${icon('<path d="M12 5v14M5 12h14"/>')}</span><span class="sc-title">Add</span></button>`);
    }
    return `
      <div class="fv-head">
        <input class="fv-title" value="${escapeHtml(f.title)}" maxlength="30" spellcheck="false" autocomplete="off" aria-label="Folder name" />
        <button type="button" class="fv-x" data-fv-close aria-label="Close">${icon('<path d="M18 6 6 18M6 6l12 12"/>')}</button>
      </div>
      <div class="fv-grid">${tiles.join("")}</div>
      <div class="fv-foot">
        ${f.items.length ? `<button type="button" class="btn btn-ghost" data-fv-ungroup>${icon(gridIcon)}Ungroup</button>` : ""}
        <span class="flex-1"></span>
        <button type="button" class="btn btn-danger" data-fv-delete>${icon(trash)}Delete folder</button>
      </div>`;
  }

  function deleteHtml(f: Folder): string {
    const n = f.items.length;
    return `
      <div class="fv-confirm">
        <span class="fv-confirm-ico danger">${icon(trash)}</span>
        <h2>Delete “${escapeHtml(f.title)}”?</h2>
        ${
          n
            ? `<label class="fv-also"><span>Also delete its ${n === 1 ? "shortcut" : `${n} shortcuts`}</span><span class="switch"><input type="checkbox" data-fv-also ${alsoLinks ? "checked" : ""} /><span></span></span></label>`
            : ""
        }
        <div class="fv-confirm-actions">
          <button type="button" class="btn btn-ghost" data-fv-cancel>Cancel</button>
          <button type="button" class="btn btn-danger solid" data-fv-confirm>Delete</button>
        </div>
      </div>`;
  }

  function ungroupHtml(f: Folder): string {
    const n = f.items.length;
    // The row may not fit them all: the rest stay in the folder.
    const room = maxShortcuts - host.list().length + 1;
    const stay = Math.max(0, n - room);
    return `
      <div class="fv-confirm">
        <span class="fv-confirm-ico">${icon(gridIcon)}</span>
        <h2>Ungroup “${escapeHtml(f.title)}”?</h2>
        ${stay ? `<p class="fv-note">The row is full, so ${stay} stay${stay === 1 ? "s" : ""} in the folder.</p>` : ""}
        <div class="fv-confirm-actions">
          <button type="button" class="btn btn-ghost" data-fv-cancel>Cancel</button>
          <button type="button" class="btn btn-primary" data-fv-confirm>Ungroup</button>
        </div>
      </div>`;
  }

  function fadeIn(el: HTMLElement, i: number): void {
    el.animate([{ opacity: 0, translate: "0 6px", filter: "blur(3px)" }, { opacity: 1, translate: "0 0", filter: "blur(0)" }], {
      duration: 300,
      delay: i * 40,
      easing: ease,
      fill: "backwards",
    });
  }

  // ---- Opening and closing: the folder's tile becomes the card. The card starts exactly over the tile's icon (its
  // size and rounding), springs out to its place while the tile itself stays empty, and its contents rise in just
  // after. Closing plays it back: the contents fade, the card shrinks into the tile, and the tile's icon pops back.

  /** The keyframe that puts the card exactly over the folder's icon in the row (or a soft "away" without one). */
  function overTile(): Keyframe {
    const ico = host.tileOf(index)?.querySelector(".sc-ico");
    const b = card.getBoundingClientRect();
    if (!ico || !b.width) return { translate: "0 0", scale: "0.92", opacity: 0 };
    const a = ico.getBoundingClientRect();
    const sx = a.width / b.width;
    const sy = a.height / b.height;
    return {
      translate: `${a.left + a.width / 2 - (b.left + b.width / 2)}px ${a.top + a.height / 2 - (b.top + b.height / 2)}px`,
      scale: `${sx} ${sy}`,
      // The tile's rounding (0.95rem on a 3rem square), undone from the scale so it looks the same.
      borderRadius: `${(15.2 / sx).toFixed(1)}px / ${(15.2 / sy).toFixed(1)}px`,
      opacity: 1,
    };
  }

  const parts = () => [...card.children] as HTMLElement[];

  /**
   * While the card grows or shrinks, its glass blur is off (`.morphing`: a slightly more solid fill instead): a blur
   * re-worked on every frame of a moving, scaling card is what made opening and closing stutter.
   */
  function morphing(animation: Animation): Animation {
    card.classList.add("morphing");
    void animation.finished.finally(() => {
      if (!card.getAnimations().some((a) => a !== animation && a.playState === "running")) card.classList.remove("morphing");
    });
    return animation;
  }

  /** Opens folder `f`, growing from its tile. `rename`: a new folder, its name selected to be typed over. */
  function open(f: number, rename = false): void {
    index = f;
    asking = null;
    alsoLinks = false;
    drawnKey = "";
    turn++;
    if (!folder()) return;
    draw();
    card.getAnimations().forEach((a) => a.cancel());
    show();
    host.modalOpened();
    if (!reduced()) {
      morphing(
        card.animate([overTile(), { translate: "0 0", scale: "1", borderRadius: getComputedStyle(card).borderRadius, opacity: 1 }], {
          duration: 460,
          easing: "cubic-bezier(0.32, 1.2, 0.5, 1)", // a touch of spring at the end
        }),
      );
      parts().forEach((el, i) =>
        el.animate([{ opacity: 0, scale: "0.96" }, { opacity: 1, scale: "1" }], {
          duration: 300,
          delay: 110 + i * 45,
          easing: ease,
          fill: "backwards",
        }),
      );
    }
    const title = card.querySelector<HTMLInputElement>(".fv-title")!;
    if (rename) {
      title.focus();
      title.select();
    } else card.querySelector<HTMLElement>(".fv-sc .sc-link, [data-fv-add]")?.focus({ preventScroll: true });
  }

  const show = () => {
    card.classList.add("open");
    card.removeAttribute("inert");
  };
  const hide = () => {
    card.classList.remove("open");
    card.setAttribute("inert", "");
  };

  function close(): void {
    if (index < 0) return;
    saveTitle();
    const tile = host.tileOf(index);
    const visible = card.classList.contains("open");
    const target = visible && !reduced() ? overTile() : null;
    index = -1;
    asking = null;
    const mine = ++turn;
    card.setAttribute("inert", "");
    const done = () => {
      if (turn !== mine) return; // opened again meanwhile
      hide();
      card.getAnimations().forEach((a) => a.cancel());
      tile?.classList.remove("fv-source");
      // The icon is back in the row: a small pop, as if the card landed in it.
      if (target) tile?.querySelector(".sc-ico")?.animate([{ scale: "0.82" }, { scale: "1.06", offset: 0.6 }, { scale: "1" }], { duration: 320, easing: ease });
    };
    if (!target) return done();
    parts().forEach((el) => el.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 120, easing: "ease-in", fill: "forwards" }));
    morphing(
      card.animate([{ translate: "0 0", scale: "1", borderRadius: getComputedStyle(card).borderRadius, opacity: 1 }, { ...target, offset: 0.88 }, { ...target, opacity: 0 }], {
        duration: 300,
        delay: 50,
        easing: "cubic-bezier(0.4, 0, 0.7, 1)",
        fill: "forwards",
      }),
    ).finished.then(done, done);
  }

  /** The editor opens in its place (never a window over a window): a quick fade away, and back when it's done. */
  function suspend(): void {
    if (index < 0 || !card.classList.contains("open")) return;
    card.setAttribute("inert", "");
    if (reduced()) return hide();
    card.animate([{ opacity: 1, scale: "1" }, { opacity: 0, scale: "0.97" }], { duration: 140, easing: "ease-in", fill: "forwards" }).finished.then(() => {
      if (card.hasAttribute("inert")) hide();
      card.getAnimations().forEach((a) => a.cancel());
    });
  }

  function resume(): void {
    if (index < 0) return;
    if (!folder()) return void (index = -1);
    card.getAnimations().forEach((a) => a.cancel());
    draw();
    show();
    if (!reduced()) card.animate([{ opacity: 0, scale: "0.97" }, { opacity: 1, scale: "1" }], { duration: 220, easing: ease });
  }

  /** Redraws after the list changed (an edit, another tab); closes if the folder is gone. */
  function refresh(): void {
    if (index < 0) return;
    if (!folder()) {
      // Its last link left, so the folder is gone: the card fades away (there's no tile to shrink into).
      index = -1;
      asking = null;
      const mine = ++turn;
      card.setAttribute("inert", "");
      const gone = () => {
        if (turn !== mine) return;
        hide();
        card.getAnimations().forEach((a) => a.cancel());
      };
      if (reduced() || !card.classList.contains("open")) return gone();
      card.animate([{ opacity: 1, scale: "1" }, { opacity: 0, scale: "0.94" }], { duration: 200, easing: "ease-in", fill: "forwards" }).finished.then(gone, gone);
      return;
    }
    if (!press) draw();
  }

  function saveTitle(): void {
    const input = card.querySelector<HTMLInputElement>(".fv-title");
    const f = folder();
    if (!input || !f) return;
    const title = input.value.trim() || "Folder";
    if (input.value !== title) input.value = title;
    if (title !== f.title) host.rename(index, title);
  }

  card.addEventListener("change", (e) => {
    const t = e.target as HTMLInputElement;
    if (t.matches(".fv-title")) saveTitle();
    if (t.matches("[data-fv-also]")) {
      alsoLinks = t.checked;
      drawnKey = keyOf(folder()); // nothing else changes: the switch keeps its slide
    }
  });
  card.addEventListener("keydown", (e) => {
    if ((e.target as HTMLElement).matches(".fv-title") && e.key === "Enter") (e.target as HTMLInputElement).blur();
  });

  card.addEventListener("click", (e) => {
    if (justDragged) return e.preventDefault();
    const t = e.target as HTMLElement;
    const edit = t.closest<HTMLElement>("[data-fv-edit]");
    if (edit) {
      e.preventDefault();
      return host.editLink(index, Number(edit.dataset.fvEdit));
    }
    if (t.closest("[data-fv-add]")) return host.addLink(index);
    if (t.closest("[data-fv-close]")) return host.close();
    const ask = t.closest("[data-fv-ungroup]") ? "ungroup" : t.closest("[data-fv-delete]") ? "delete" : null;
    if (ask) {
      saveTitle();
      asking = ask;
      alsoLinks = false;
      return draw();
    }
    if (t.closest("[data-fv-cancel]")) {
      asking = null;
      return draw();
    }
    if (t.closest("[data-fv-confirm]")) {
      const f = index;
      const next = asking === "ungroup" ? ungroup(host.list(), f) : deleteFolder(host.list(), f, !alsoLinks);
      host.close();
      return host.setList(next);
    }
  });

  // ---- Drag a link: within the card to reorder (resting on another link takes its place, as in the row); out of the
  // card to take it out of the folder. While dragging, the footer says so; outside, the rest of the card fades and it
  // says what letting go does. A drop is kept in the card as it is (tiles renumbered, not redrawn): nothing blinks.

  let press: { tile: HTMLElement; id: number; x: number; y: number; dx: number; dy: number; dragging: boolean } | null = null;
  let justDragged = false;
  let outside = false;

  const grid = () => card.querySelector<HTMLElement>(".fv-grid")!;
  const linkTiles = () => [...grid().querySelectorAll<HTMLElement>(".fv-sc")];
  const sorter = createSorter(card, linkTiles);

  /** The footer's place shows how to take a link out while one is dragged. */
  function dragHint(text: string): void {
    let el = card.querySelector<HTMLElement>(".fv-drag-hint");
    if (!el && text) {
      el = document.createElement("p");
      el.className = "fv-drag-hint";
      card.append(el);
    }
    if (el) el.textContent = text;
  }

  card.addEventListener("pointerdown", (e) => {
    if (e.button !== 0 || (e.target as HTMLElement).closest(".sc-edit")) return;
    const tile = (e.target as HTMLElement).closest<HTMLElement>(".fv-sc");
    if (!tile) return;
    const box = tile.getBoundingClientRect();
    press = { tile, id: e.pointerId, x: e.clientX, y: e.clientY, dx: e.clientX - box.left, dy: e.clientY - box.top, dragging: false };
  });

  card.addEventListener("pointermove", (e) => {
    if (!press || e.pointerId !== press.id) return;
    const { tile } = press;
    if (!press.dragging) {
      if (Math.hypot(e.clientX - press.x, e.clientY - press.y) < 6) return;
      press.dragging = true;
      tile.setPointerCapture(e.pointerId);
      tile.classList.add("held");
      card.classList.add("sorting");
      dragHint("Drag out of the folder to move it to the row");
    }
    const box = card.getBoundingClientRect();
    const out = e.clientX < box.left || e.clientX > box.right || e.clientY < box.top || e.clientY > box.bottom;
    if (out !== outside) {
      outside = out;
      card.classList.toggle("dropping-out", outside);
      dragHint(outside ? "Let go to move it to the row" : "Drag out of the folder to move it to the row");
    }
    sorter.rest(tile, outside ? null : tileAt(e.clientX, e.clientY, ".fv-sc", tile));
    const home = homeOf(tile);
    tile.style.translate = `${e.clientX - press.dx - home.left}px ${e.clientY - press.dy - home.top}px`;
  });

  function endPress(e: PointerEvent): void {
    if (!press || e.pointerId !== press.id) return;
    const { tile, dragging } = press;
    press = null;
    sorter.stop();
    if (!dragging) return;
    justDragged = true;
    setTimeout(() => (justDragged = false), 0);
    card.classList.remove("sorting", "dropping-out");
    card.querySelector(".fv-drag-hint")?.remove();
    const f = folder();
    if (!f) return;
    const order = linkTiles().map((t) => Number(t.dataset.i));
    const reordered = updateFolder(host.list(), index, (x) => ({ ...x, items: order.map((i) => x.items[i]!) }));
    const wasOutside = outside;
    outside = false;
    if (wasOutside) {
      // Out of the folder: into the row, right after it. The last one out takes the folder's place (no empty folders).
      const next = takeOut(reordered, index, order.indexOf(Number(tile.dataset.i)));
      if (next === reordered) return settle(tile, () => keep(reordered)); // the row is full: it stays
      tile.animate([{ opacity: 1 }, { opacity: 0, scale: "0.8" }], { duration: 160, fill: "forwards" }).finished.then(() => {
        if (f.items.length > 1) {
          // Only that tile goes: the rest stay as they are and glide together.
          sorter.glide(() => tile.remove(), tile);
          keep(next);
        } else host.setList(next);
      });
      return;
    }
    settle(tile, () => {
      if (order.some((key, i) => key !== i)) keep(reordered);
    });
  }

  /**
   * Saves a list whose links in this folder are already on screen in its order: the tiles are renumbered and the card
   * marked as drawn for it, so saving doesn't rebuild the card (no blink, no icons loading again).
   */
  function keep(list: Shortcut[]): void {
    linkTiles().forEach((t, i) => {
      t.dataset.i = String(i);
      t.querySelector<HTMLElement>("[data-fv-edit]")!.dataset.fvEdit = String(i);
    });
    const f = list[index];
    if (f && isFolder(f)) drawnKey = JSON.stringify([index, f.items, asking, alsoLinks]);
    host.setList(list);
  }

  function settle(tile: HTMLElement, done: () => void): void {
    const from = tile.style.translate || "0px 0px";
    tile.style.translate = "";
    tile.classList.replace("held", "settling");
    tile.animate([{ translate: from, scale: "1.08" }, { translate: "0 0", scale: "1" }], { duration: 260, easing: ease }).finished.finally(() => {
      tile.classList.remove("settling");
      done();
    });
  }

  card.addEventListener("pointerup", endPress);
  card.addEventListener("pointercancel", endPress);

  /** Esc or the backdrop while the card asks: back to the folder. False when there's no question to go back from. */
  function back(): boolean {
    if (index < 0 || !asking) return false;
    asking = null;
    draw();
    return true;
  }

  /** The open folder moved in the list (links before it left the row): keep showing it at its new place. */
  function follow(at: number): void {
    if (index < 0 || at < 0 || at === index) return;
    host.tileOf(index)?.classList.remove("fv-source");
    index = at;
  }

  return {
    open,
    close,
    back,
    follow,
    refresh,
    suspend,
    resume,
    get isOpen() {
      return index >= 0;
    },
  };
}
