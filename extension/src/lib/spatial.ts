/** A control's place on screen (a DOMRect, or a plain object in the tests). */
export interface Box {
  left: number;
  top: number;
  right: number;
  bottom: number;
  width: number;
  height: number;
}

/**
 * For the arrow keys (newtab/keyboard.ts): which of [rects] is nearest from [from] toward [key] (ArrowLeft and so on),
 * straight ahead first, then the least sideways; -1 when none lies that way.
 */
export function nearest(from: Box, key: string, rects: Box[]): number {
  const dx = key === "ArrowRight" ? 1 : key === "ArrowLeft" ? -1 : 0;
  const dy = key === "ArrowDown" ? 1 : key === "ArrowUp" ? -1 : 0;
  const cx = from.left + from.width / 2;
  const cy = from.top + from.height / 2;
  let best = -1;
  let bestScore = Infinity;
  rects.forEach((r, i) => {
    const x = r.left + r.width / 2;
    const y = r.top + r.height / 2;
    // How far ahead (edge to edge, so wide and narrow neighbours compare fairly), and how far off to the side.
    const ahead = dx ? (dx > 0 ? r.left - from.right : from.left - r.right) : dy > 0 ? r.top - from.bottom : from.top - r.bottom;
    const center = dx ? (x - cx) * dx : (y - cy) * dy;
    // Only past the edge: a button lying over this one (a shortcut's ⋯) is for Tab, not for the arrows.
    if (center <= 1 || ahead < -4) return;
    const side = dx ? Math.max(0, r.top - from.bottom, from.top - r.bottom) : Math.max(0, r.left - from.right, from.left - r.right);
    const offset = dx ? Math.abs(y - cy) : Math.abs(x - cx);
    const score = Math.max(0, ahead) + side * 4 + offset * 0.5;
    if (score < bestScore) {
      bestScore = score;
      best = i;
    }
  });
  return best;
}
