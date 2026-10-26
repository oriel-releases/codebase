/**
 * The keyboard, on the new tab and in the toolbar popup:
 * - The arrow keys move the focus to the nearest control in that direction (across the shortcuts, the settings menu
 *   and its page, a folder, a dialog's buttons). Enter or Space then presses it, as always.
 * - A dialog that opens takes the focus, keeps Tab inside it, and gives the focus back to where it was once it closes.
 * Dialogs open and close by their `inert` attribute (see main.ts, folder-view.ts, shortcuts-row.ts), so they are found
 * by watching it rather than each one calling in here.
 */

import { nearest } from "../lib/spatial";

const focusable = 'a[href], button, input, select, textarea, [tabindex]:not([tabindex="-1"])';

/** Controls that use the arrow keys themselves: the caret in a text field, a slider, a dropdown. */
function ownsArrows(el: Element, key: string): boolean {
  if (el instanceof HTMLSelectElement || el instanceof HTMLTextAreaElement || (el as HTMLElement).isContentEditable) return true;
  if (!(el instanceof HTMLInputElement)) return false;
  if (el.type === "range") return true;
  if (el.type === "checkbox" || el.type === "radio" || el.type === "button" || el.type === "submit") return false;
  // A text field keeps Left and Right for its caret; Up and Down leave it (the search box down to the shortcuts).
  return key === "ArrowLeft" || key === "ArrowRight";
}

function usable(el: HTMLElement): boolean {
  if ((el as HTMLButtonElement).disabled || el.closest("[inert], [hidden], [aria-hidden='true']")) return false;
  const r = el.getBoundingClientRect();
  if (r.width < 2 || r.height < 2) return false;
  const style = getComputedStyle(el);
  return style.visibility !== "hidden" && Number(style.opacity) > 0.05;
}

/** Open dialogs, the last opened last. */
const open: { dialog: HTMLElement; back: HTMLElement | null }[] = [];

/** Where the arrows and Tab move: the dialog on top, or the page. */
function scope(): ParentNode {
  return open.at(-1)?.dialog ?? document;
}

function candidates(): HTMLElement[] {
  return [...scope().querySelectorAll<HTMLElement>(focusable)].filter(usable);
}

function onArrow(e: KeyboardEvent): void {
  if (!e.key.startsWith("Arrow") || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey || e.defaultPrevented) return;
  const active = document.activeElement as HTMLElement | null;
  const list = candidates();
  if (!list.length) return;
  // Nothing focused yet: the first arrow press lands on the first control of the page (or of the open dialog).
  if (!active || active === document.body || !scope().contains(active)) {
    const first = list.find((el) => el.closest("#shortcuts")) ?? list[0];
    e.preventDefault();
    first.focus();
    return;
  }
  if (ownsArrows(active, e.key)) return;
  const others = list.filter((el) => el !== active && !el.contains(active));
  const i = nearest(active.getBoundingClientRect(), e.key, others.map((el) => el.getBoundingClientRect()));
  if (i < 0) return;
  e.preventDefault();
  others[i].focus();
  others[i].scrollIntoView({ block: "nearest", inline: "nearest" });
}

/** Tab and Shift+Tab go round the dialog on top instead of out to the page behind it. */
function onTab(e: KeyboardEvent): void {
  const top = open.at(-1)?.dialog;
  if (e.key !== "Tab" || !top) return;
  const list = candidates();
  if (!list.length) return;
  const i = list.indexOf(document.activeElement as HTMLElement);
  if (i < 0 || (e.shiftKey && i === 0) || (!e.shiftKey && i === list.length - 1)) {
    e.preventDefault();
    list[e.shiftKey ? list.length - 1 : 0].focus();
  }
}

function opened(dialog: HTMLElement): void {
  if (open.some((o) => o.dialog === dialog)) return;
  const back = document.activeElement instanceof HTMLElement && document.activeElement !== document.body ? document.activeElement : null;
  open.push({ dialog, back });
  // After the dialog has drawn (and its own code had the chance to focus a field), the focus goes in if it isn't yet.
  requestAnimationFrame(() =>
    requestAnimationFrame(() => {
      if (dialog.hasAttribute("inert") || dialog.contains(document.activeElement)) return;
      const target = dialog.querySelector<HTMLElement>('[aria-current="page"], [autofocus]') ?? [...dialog.querySelectorAll<HTMLElement>(focusable)].find(usable);
      (target ?? dialog).focus({ preventScroll: true });
    }),
  );
}

function closed(dialog: HTMLElement): void {
  const i = open.findIndex((o) => o.dialog === dialog);
  if (i < 0) return;
  const [{ back }] = open.splice(i, 1);
  // Only when the focus was lost with it: a dialog that opened another one (a folder's editor) leaves it there.
  const lost = !document.activeElement || document.activeElement === document.body || dialog.contains(document.activeElement);
  if (lost && back?.isConnected && usable(back)) back.focus({ preventScroll: true });
  else if (lost) open.at(-1)?.dialog.focus({ preventScroll: true });
}

/** Turns it all on, once. [dialogs]: the page's dialogs, shown and hidden by their `inert` attribute. */
export function setUpKeyboard(dialogs: HTMLElement[]): void {
  document.addEventListener("keydown", onTab);
  // Bubbling, after the page's own handlers: a slider, the shortcut drag or a dialog can claim an arrow key first.
  document.addEventListener("keydown", onArrow);
  for (const dialog of dialogs) {
    if (!dialog.hasAttribute("tabindex")) dialog.tabIndex = -1; // so it can hold the focus when nothing in it can
    if (!dialog.hasAttribute("inert")) opened(dialog);
    new MutationObserver(() => (dialog.hasAttribute("inert") ? closed(dialog) : opened(dialog))).observe(dialog, {
      attributes: true,
      attributeFilter: ["inert"],
    });
  }
}
