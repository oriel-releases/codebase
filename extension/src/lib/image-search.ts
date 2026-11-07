import { asGoogleLines } from "./search.ts";

/** Google Lens takes an uploaded photo (a form post) and keeps the text of `q` next to it, as "add to your search". */
const lensUpload = "https://lens.google.com/v3/upload";
const lensDefaults = { hl: "en", re: "df", st: "1", ep: "gsbubb" };

/** The name of the file field Lens expects in the post. */
export const lensFileField = "encoded_image";

/** The first image among files (pasted, dropped or picked), or null. */
export function firstImage(files: Iterable<File>): File | null {
  for (const file of files) if (file.type.startsWith("image/")) return file;
  return null;
}

/** Where the form with the photo is posted; the text, if any, rides along as the question about the photo. */
export function lensUrl(text: string): string {
  const params = new URLSearchParams(lensDefaults);
  const question = text.trim();
  if (question) params.set("q", asGoogleLines(question));
  return `${lensUpload}?${params}`;
}
