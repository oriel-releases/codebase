/**
 * The big clock, drawn a character at a time: when the time changes, only the digits that changed move, blurring as
 * they go. Going forward the new digit drops in from above and the old one sinks away; going back, the other way.
 * Characters are matched from the right, so 9:21 to 10:21 only adds the "1" (its box opening up, so the clock glides
 * aside instead of jumping) and 10:21 to 9:21 closes it. Transforms, opacity, blur and that one width only (cheap).
 */
export function showTime(el: HTMLElement, text: string, forward = true, animated = true): void {
  const before = el.dataset.time ?? "";
  if (text === before) return;
  el.dataset.time = text;
  el.setAttribute("aria-label", text);
  if (!animated || before === "" || text === "" || matchMedia("(prefers-reduced-motion: reduce)").matches) {
    el.replaceChildren(...[...text].map(box));
    return;
  }
  const lift = forward ? -1 : 1; // where a new digit comes from: above when the time moves on
  const boxes = [...el.querySelectorAll<HTMLElement>(".clock-ch:not(.leaving)")];
  const extra = text.length - boxes.length;
  let order = 0;
  // Fewer characters: the first ones leave and their boxes close.
  boxes.slice(0, Math.max(0, -extra)).forEach((gone) => {
    gone.classList.add("leaving");
    leave(gone.firstElementChild as HTMLElement, lift, order);
    resize(gone, gone.offsetWidth, 0, order++).finished.finally(() => gone.remove());
  });
  // More characters: new boxes open at the front and their digits come in.
  const first = boxes[Math.max(0, -extra)] ?? null;
  for (let i = 0; i < extra; i++) {
    const added = box(text[i]);
    el.insertBefore(added, first);
    resize(added, 0, added.offsetWidth, order);
    enter(added.firstElementChild as HTMLElement, lift, order++);
  }
  // The rest, matched from the right: only a changed digit moves.
  boxes.slice(Math.max(0, -extra)).forEach((kept, k) => {
    const ch = text[Math.max(0, extra) + k];
    const digit = kept.firstElementChild as HTMLElement;
    if (digit.textContent === ch) return;
    // The old digit leaves as a copy beside the new one, so each moves on its own.
    const ghost = document.createElement("span");
    ghost.className = "clock-ghost";
    ghost.textContent = digit.textContent;
    kept.append(ghost);
    digit.textContent = ch;
    leave(ghost, lift, order).finished.finally(() => ghost.remove());
    enter(digit, lift, order++);
  });
}

/** A character's box; a digit's is exactly as wide as "0" (`.digit`), so the clock never shifts as digits change. */
function box(ch: string): HTMLElement {
  const el = document.createElement("span");
  el.className = /\d/.test(ch) ? "clock-ch digit" : "clock-ch";
  el.setAttribute("aria-hidden", "true");
  const inner = document.createElement("span");
  inner.textContent = ch;
  el.append(inner);
  return el;
}

const distance = 0.12; // em
const easing = "cubic-bezier(0.2, 0.8, 0.2, 1)";

function enter(digit: HTMLElement, lift: number, order: number): Animation {
  return digit.animate(
    [{ translate: `0 ${lift * distance}em`, opacity: 0, filter: "blur(12px)" }, { translate: "0 0", opacity: 1, filter: "blur(0)" }],
    { duration: 1100, delay: order * 80 + 250, easing: "cubic-bezier(0.25, 0.6, 0.25, 1)", fill: "backwards" },
  );
}

function leave(digit: HTMLElement, lift: number, order: number): Animation {
  return digit.animate(
    [{ translate: "0 0", opacity: 1, filter: "blur(0)" }, { translate: `0 ${-lift * distance}em`, opacity: 0, filter: "blur(12px)" }],
    { duration: 800, delay: order * 80, easing: "cubic-bezier(0.45, 0, 0.55, 1)", fill: "forwards" },
  );
}

function resize(box: HTMLElement, from: number, to: number, order: number): Animation {
  return box.animate([{ width: `${from}px` }, { width: `${to}px` }], { duration: 1000, delay: order * 80, easing, fill: "both" });
}
