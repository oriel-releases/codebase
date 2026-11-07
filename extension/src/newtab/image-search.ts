import { firstImage, lensFileField, lensUrl } from "../lib/image-search";
import { openLightbox } from "./lightbox";

/**
 * A photo in the search bar: Ctrl+V, or drop it on the page. It shows above the field, inside the same pill; searching then sends the photo (and the words, as a question about it) to Google Lens.
 */
export interface ImageSearch {
  /** Sends the photo with the text; false when there is no photo. */
  submit(text: string): boolean;
}

export function createImageSearch(form: HTMLFormElement, input: HTMLTextAreaElement): ImageSearch {
  const preview = form.querySelector<HTMLElement>("#q-preview")!;
  const thumb = preview.querySelector<HTMLImageElement>("img")!;
  let image: File | null = null;
  let objectUrl = "";

  const dropHint = "Drop the photo here";
  const restingHint = () => (image ? "Ask about this photo" : "Search the web");

  const searchIsOff = () => form.style.display === "none";

  function attach(file: File): void {
    // A second photo replaces the first in place: the box stays open (clearing first made it shut and open again).
    const previous = objectUrl;
    image = file;
    objectUrl = URL.createObjectURL(file);
    thumb.src = objectUrl;
    if (previous) URL.revokeObjectURL(previous);
    thumb.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 200, easing: "ease-out" });
    preview.dataset.on = "true";
    form.dataset.hasImage = "true";
    input.placeholder = restingHint();
    input.focus();
  }

  function clear(): void {
    image = null;
    if (objectUrl) URL.revokeObjectURL(objectUrl);
    objectUrl = "";
    thumb.removeAttribute("src");
    delete preview.dataset.on;
    delete form.dataset.hasImage;
    input.placeholder = restingHint();
  }

  function showDrop(on: boolean): void {
    if (on) form.dataset.drop = "true";
    else delete form.dataset.drop;
    input.placeholder = on ? dropHint : restingHint();
  }

  // The whole pill is the field: a click on its padding, the icon or the gap lands in the input.
  form.addEventListener("pointerdown", (e) => {
    if ((e.target as HTMLElement).closest("button, textarea, img")) return;
    e.preventDefault();
    input.focus();
  });

  preview.querySelector("button")!.addEventListener("click", () => {
    clear();
    input.focus();
  });
  thumb.addEventListener("click", () => openLightbox(thumb));

  // Paste anywhere on the page (the settings dialog has its own fields, so it keeps its pastes).
  document.addEventListener("paste", (e) => {
    if (searchIsOff() || document.body.classList.contains("modal-open")) return;
    const file = firstImage(e.clipboardData?.files ?? []);
    if (!file) return;
    e.preventDefault();
    attach(file);
  });

  document.addEventListener("dragover", (e) => {
    if (searchIsOff() || !e.dataTransfer?.types.includes("Files")) return;
    e.preventDefault();
    showDrop(true);
  });
  document.addEventListener("dragleave", (e) => {
    if (e.relatedTarget === null) showDrop(false);
  });
  document.addEventListener("drop", (e) => {
    showDrop(false);
    const file = firstImage(e.dataTransfer?.files ?? []);
    if (searchIsOff() || !file) return;
    e.preventDefault();
    attach(file);
  });

  // Backspace in an empty field takes the photo back out.
  input.addEventListener("keydown", (e) => {
    if (e.key === "Backspace" && image && !input.value) clear();
  });

  return {
    submit(text) {
      if (!image) return false;
      // A form post reaches Google from this page with no extra permission; the answer opens in this tab.
      const post = document.createElement("form");
      post.method = "POST";
      post.enctype = "multipart/form-data";
      post.action = lensUrl(text);
      post.hidden = true;
      const field = document.createElement("input");
      field.type = "file";
      field.name = lensFileField;
      const files = new DataTransfer();
      files.items.add(image);
      field.files = files.files;
      post.append(field);
      document.body.append(post);
      post.submit();
      return true;
    },
  };
}
