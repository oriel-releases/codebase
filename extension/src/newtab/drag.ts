import { ease } from "./tiles";

/**
 * The moving parts shared by dragging in the row and in an open folder.
 *
 * Reordering is by place, not by which half of a tile the pointer is on: resting on a tile for a moment moves the held
 * one into that tile's place (after it when coming from before, before it when coming from after). The tile under the
 * pointer then glides away, and the pointer is over the held tile's own new place, so nothing flips back and forth.
 * While tiles glide, no new move starts (their boxes are still moving under the pointer).
 */
export function createSorter(container: HTMLElement, tiles: () => HTMLElement[]) {
  let resting: HTMLElement | null = null;
  let timer = 0;
  let calmAt = 0;

  /** Moves tiles in the page and lets every tile that moved (but the one held) glide from where it was (FLIP). */
  function glide(change: () => void, held: HTMLElement): void {
    const others = [...container.querySelectorAll<HTMLElement>(".sc")].filter((t) => t !== held);
    const before = new Map(others.map((t) => [t, t.getBoundingClientRect()]));
    change();
    for (const t of others) {
      const a = before.get(t)!;
      const b = t.getBoundingClientRect();
      if (a.left !== b.left || a.top !== b.top) t.animate([{ translate: `${a.left - b.left}px ${a.top - b.top}px` }, { translate: "0 0" }], { duration: 240, easing: ease });
    }
    calmAt = performance.now() + 200;
  }

  /** The pointer is over `over` (null: over no tile): after a short rest there, `held` takes its place. */
  function rest(held: HTMLElement, over: HTMLElement | null, delay = 110): void {
    if (over === resting) return;
    resting = over;
    clearTimeout(timer);
    if (!over) return;
    const move = () => {
      if (resting !== over || !over.isConnected) return;
      const wait = calmAt - performance.now();
      if (wait > 0) return void (timer = window.setTimeout(move, wait));
      const list = tiles();
      const from = list.indexOf(held);
      const to = list.indexOf(over);
      if (from < 0 || to < 0 || from === to) return;
      glide(() => container.insertBefore(held, from < to ? over.nextElementSibling : over), held);
      resting = null; // the same tile can be rested on again (to move back)
    };
    timer = window.setTimeout(move, delay);
  }

  function stop(): void {
    resting = null;
    clearTimeout(timer);
  }

  return { rest, stop, glide };
}

/** Where `tile` sits in its container, ignoring the offset it's drawn at while held. */
export function homeOf(tile: HTMLElement): DOMRect {
  const offset = tile.style.translate;
  tile.style.translate = "";
  const box = tile.getBoundingClientRect();
  tile.style.translate = offset;
  return box;
}

/** The tile under the pointer, other than the held one. */
export function tileAt(x: number, y: number, selector: string, held: HTMLElement): HTMLElement | null {
  return (document
    .elementsFromPoint(x, y)
    .map((el) => el.closest(selector))
    .find((el) => el && el !== held) ?? null) as HTMLElement | null;
}

/** A small label over the held tile saying what letting go will do; empty text hides it. */
export function hint(tile: HTMLElement, text: string): void {
  let label = tile.querySelector<HTMLElement>(".sc-hint");
  if (!label) {
    label = document.createElement("span");
    label.className = "sc-hint";
    tile.append(label);
  }
  if (text) label.textContent = text;
  label.classList.toggle("show", !!text);
}
