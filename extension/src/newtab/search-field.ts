const maxRows = 5;

/** The search field grows with its lines. Enter searches, Shift+Enter starts a new line. */
export function setUpSearchField(form: HTMLFormElement, field: HTMLTextAreaElement): void {
  const fit = () => {
    field.style.height = "auto";
    field.style.height = `${field.scrollHeight}px`;
  };
  field.addEventListener("input", fit);
  field.addEventListener("keydown", (e) => {
    if (e.key !== "Enter" || e.shiftKey || e.isComposing) return;
    e.preventDefault();
    form.requestSubmit();
  });
  field.style.maxHeight = `${maxRows * 1.4}em`;
}
