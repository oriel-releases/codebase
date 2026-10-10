/**
 * Picks white or near-black for text that sits directly on the photo, by measuring how bright the photo is
 * right behind each element. Elements opt in with the data-adapt attribute.
 */

interface Sample {
  data: Uint8ClampedArray;
  width: number;
  height: number;
}

let sample: Sample | null = null;
const size = 96;

/** Reads the photo the way it is shown (cropped to the window, "cover") into a tiny grid. Returns false if the browser won't let us read it. */
export function measure(img: HTMLImageElement): boolean {
  try {
    const width = size;
    const height = Math.max(1, Math.round((size * window.innerHeight) / window.innerWidth));
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext("2d", { willReadFrequently: true })!;
    const scale = Math.max(width / img.naturalWidth, height / img.naturalHeight);
    const w = img.naturalWidth * scale;
    const h = img.naturalHeight * scale;
    ctx.drawImage(img, (width - w) / 2, (height - h) / 2, w, h);
    sample = { data: ctx.getImageData(0, 0, width, height).data, width, height };
    return true;
  } catch {
    sample = null; // a photo that isn't CORS-readable taints the canvas
    return false;
  }
}

export function forget(): void {
  sample = null;
}

/** Sets data-tone="dark" on elements over a bright photo (so they use dark text), "light" otherwise. `dim` is the 0 to 1 darkening. */
export function applyTone(dim: number): void {
  document.querySelectorAll<HTMLElement>("[data-adapt]").forEach((el) => {
    el.dataset.tone = sample && brightness(el) * (1 - dim) > 0.6 ? "dark" : "light";
  });
}

function brightness(el: HTMLElement): number {
  const s = sample!;
  const r = el.getBoundingClientRect();
  if (r.width === 0 || r.height === 0) return 0;
  const x0 = clamp(Math.floor((r.left / window.innerWidth) * s.width), s.width - 1);
  const x1 = clamp(Math.ceil((r.right / window.innerWidth) * s.width), s.width);
  const y0 = clamp(Math.floor((r.top / window.innerHeight) * s.height), s.height - 1);
  const y1 = clamp(Math.ceil((r.bottom / window.innerHeight) * s.height), s.height);
  let sum = 0;
  let count = 0;
  for (let y = y0; y < Math.max(y1, y0 + 1); y++) {
    for (let x = x0; x < Math.max(x1, x0 + 1); x++) {
      const i = (y * s.width + x) * 4;
      sum += 0.2126 * s.data[i] + 0.7152 * s.data[i + 1] + 0.0722 * s.data[i + 2];
      count++;
    }
  }
  return count ? sum / count / 255 : 0;
}

const clamp = (v: number, max: number) => Math.max(0, Math.min(max, v));
