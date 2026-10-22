import { ageOn, birthdayOf, DEFAULT_AGE, daysIn, isBirthday, MAX_AGE, MIN_AGE } from "../lib/age";

/** What the birthday dialog needs from the page. */
export interface BirthdayDialogHost {
  /** Opened: dim the page behind (the shared backdrop). */
  modalOpened(): void;
  /** Saved or removed (null): keep it and redraw. */
  save(birthday: string | null): void;
  close(): void;
}

const months = Array.from({ length: 12 }, (_, i) => new Date(2000, i, 1).toLocaleDateString(undefined, { month: "long" }));

/**
 * Your birthday, for the Age clock without an account (kept in this browser only), in the same glass card as the
 * shortcut editor: day, month and year, and the age they make. The website's /me has the same window for accounts.
 */
export function createBirthdayDialog(dialog: HTMLElement, host: BirthdayDialogHost) {
  let saved: string | null = null;

  function open(birthday: string | null): void {
    saved = birthday;
    const [y, m, d] = birthday && isBirthday(birthday) ? birthday.split("-").map(Number) : [new Date().getFullYear() - DEFAULT_AGE, 0, 0];
    const thisYear = new Date().getFullYear();
    const years = Array.from({ length: MAX_AGE - MIN_AGE + 1 }, (_, i) => thisYear - MIN_AGE - i);
    const option = (value: number, text: string | number, chosen: number) => `<option value="${value}" ${value === chosen ? "selected" : ""}>${text}</option>`;
    dialog.innerHTML = `
    <h2>Your birthday</h2>
    <form class="sc-form" novalidate>
      <div class="bday-row">
        <select class="field" name="day" aria-label="Day"><option value="0" disabled ${d ? "" : "selected"}>Day</option>${Array.from({ length: 31 }, (_, i) => option(i + 1, i + 1, d!)).join("")}</select>
        <select class="field" name="month" aria-label="Month"><option value="0" disabled ${m ? "" : "selected"}>Month</option>${months.map((name, i) => option(i + 1, name, m!)).join("")}</select>
        <select class="field" name="year" aria-label="Year">${years.map((year) => option(year, year, y!)).join("")}</select>
      </div>
      <p class="bday-age" aria-live="polite"></p>
      <p class="sc-error" role="alert"></p>
      <div class="sc-actions">
        ${birthday ? `<button type="button" class="btn btn-danger" data-remove>Remove</button>` : ""}
        <span class="flex-1"></span>
        <button type="button" class="btn btn-ghost" data-cancel>Cancel</button>
        <button type="submit" class="btn btn-primary">Save</button>
      </div>
    </form>`;
    update();
    dialog.classList.add("open");
    dialog.removeAttribute("inert");
    host.modalOpened();
    dialog.querySelector<HTMLSelectElement>(d ? '[name="year"]' : '[name="day"]')!.focus();
  }

  function close(): void {
    dialog.classList.remove("open");
    dialog.setAttribute("inert", "");
  }

  const field = (name: string) => Number(dialog.querySelector<HTMLSelectElement>(`[name="${name}"]`)?.value ?? 0);
  const chosen = () => {
    const [d, m, y] = [field("day"), field("month"), field("year")];
    return d && m ? birthdayOf(y, m, Math.min(d, daysIn(y, m))) : null;
  };

  /** A shorter month pulls a too-late day back to its last one; the age under the fields follows the choice. */
  function update(): void {
    const [m, y] = [field("month"), field("year")];
    const daySelect = dialog.querySelector<HTMLSelectElement>('[name="day"]');
    if (daySelect && m) {
      const last = daysIn(y, m);
      for (const o of daySelect.options) if (o.value !== "0") o.disabled = Number(o.value) > last;
      if (Number(daySelect.value) > last) daySelect.value = String(last);
    }
    const b = chosen();
    dialog.querySelector(".bday-age")!.textContent = b && isBirthday(b) ? `You're ${ageOn(b)}` : "";
  }

  dialog.addEventListener("change", update);
  dialog.addEventListener("submit", (e) => {
    e.preventDefault();
    const b = chosen();
    if (!b || !isBirthday(b)) {
      dialog.querySelector(".sc-error")!.textContent = "Pick the day and the month.";
      return;
    }
    host.save(b);
    host.close();
  });
  dialog.addEventListener("click", (e) => {
    const target = e.target as HTMLElement;
    if (target.closest("[data-cancel]")) host.close();
    if (target.closest("[data-remove]") && saved) {
      host.save(null);
      host.close();
    }
  });

  return { open, close };
}
