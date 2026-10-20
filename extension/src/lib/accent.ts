import type { Photo } from "./photo";

const hex = /^#[0-9a-f]{6}$/i;

/**
 * The accent color for the page's controls. Following the devices: the vivid color the desktop app picked from the
 * wallpaper (the same one it sets as the Windows accent), only while the page shows that very photo. Its own photos:
 * the vivid color stored with each pool photo.
 */
export function accentFor(photo: Photo, followsDesktop: boolean, desktopPhotoId: string | undefined): string | undefined {
  if (!followsDesktop) return readableAccent(photo.accent);
  return desktopPhotoId === photo.id ? readableAccent(photo.color) : undefined;
}

/**
 * The same hue, lifted so it stands out on the dark dialog: a photo's color can be near-black (a night sky gives a dark
 * navy), which would make the menu marker and the switches vanish. Colors bright enough already are kept as they are.
 */
export function readableAccent(color: string | undefined): string | undefined {
  if (!color || !hex.test(color)) return undefined;
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(color.slice(i, i + 2), 16) / 255);
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const lin = (v: number) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
  if (0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b) >= 0.1) return color; // bright enough already
  let l = (max + min) / 2;
  const d = max - min;
  let s = d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1));
  let h = d === 0 ? 0 : max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  h = (h * 60 + 360) % 360;
  l = 0.62;
  s = Math.max(s, 0.45);
  const c = (1 - Math.abs(2 * l - 1)) * s;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = l - c / 2;
  const [r1, g1, b1] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
  return "#" + [r1, g1, b1].map((v) => Math.round((v + m) * 255).toString(16).padStart(2, "0")).join("");
}
