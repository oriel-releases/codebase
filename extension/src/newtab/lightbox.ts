/**
 * Shows a photo full screen: it grows from where the small one sits to the middle of the page, and shrinks back to it
 * on a click, Escape or the close button.
 */
const growMs = 320;
const screenShare = 0.92;
const easing = "cubic-bezier(0.2, 0.8, 0.2, 1)";

export function openLightbox(from: HTMLImageElement): void {
  const start = from.getBoundingClientRect();
  const fit = Math.min((innerWidth * screenShare) / from.naturalWidth, (innerHeight * screenShare) / from.naturalHeight);
  const width = from.naturalWidth * fit;
  const height = from.naturalHeight * fit;
  const end = { left: (innerWidth - width) / 2, top: (innerHeight - height) / 2, width, height };

  const veil = document.createElement("div");
  veil.className = "lightbox";
  // Only the dark veil fades; the photo itself never goes transparent.
  const shade = document.createElement("div");
  shade.className = "lightbox-veil";
  const big = document.createElement("img");
  big.src = from.src;
  big.alt = from.alt;
  const close = document.createElement("button");
  close.type = "button";
  close.className = "lightbox-close";
  close.setAttribute("aria-label", "Close");
  close.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M6 6l12 12M18 6 6 18" /></svg>';
  veil.append(shade, big, close);
  document.body.append(veil);

  const smallRadius = getComputedStyle(from).borderRadius;
  const place = (box: { left: number; top: number; width: number; height: number }, radius: string) => ({
    left: `${box.left}px`,
    top: `${box.top}px`,
    width: `${box.width}px`,
    height: `${box.height}px`,
    borderRadius: radius,
  });
  const options = { duration: growMs, easing, fill: "forwards" as const };
  from.style.visibility = "hidden";
  big.animate([place(start, smallRadius), place(end, "0px")], options);
  shade.animate([{ opacity: 0 }, { opacity: 1 }], options);
  close.animate([{ opacity: 0 }, { opacity: 1 }], options);

  let closing = false;
  function shut(): void {
    if (closing) return;
    closing = true;
    document.removeEventListener("keydown", onKey, true);
    // The small photo may have moved or gone while it was open.
    const back = from.isConnected ? from.getBoundingClientRect() : start;
    big.animate([place(end, "0px"), place(back, smallRadius)], options);
    close.animate([{ opacity: 1 }, { opacity: 0 }], options);
    shade.animate([{ opacity: 1 }, { opacity: 0 }], options).onfinish = () => {
      from.style.visibility = "";
      veil.remove();
    };
  }
  function onKey(e: KeyboardEvent): void {
    if (e.key !== "Escape") return;
    e.stopPropagation();
    shut();
  }
  document.addEventListener("keydown", onKey, true);
  veil.addEventListener("click", shut);
}
