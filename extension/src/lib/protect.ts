/** Where a photo is shown: right-clicking or dragging there gives no way to save it. */
const photoArea = "img, .home-photo, .acct-banner, #bg";

/** Blocks the context menu and dragging on the page's images (CSS already lets the pointer through them). */
export function protectImages(): void {
  const block = (e: Event) => {
    if (e.target instanceof Element && e.target.closest(photoArea)) e.preventDefault();
  };
  document.addEventListener("contextmenu", block);
  document.addEventListener("dragstart", block);
}
