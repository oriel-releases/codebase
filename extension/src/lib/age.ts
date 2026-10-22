/**
 * Pure age logic for the "Age" clock: no DOM, so it can be tested on its own. A birthday is "YYYY-MM-DD", the same
 * as the account's users/{uid}.birthday (set on the website, website/src/lib/birthday.ts has the same rules).
 */

export const MIN_AGE = 5;
export const MAX_AGE = 120;
/** The year the picker starts on when no birthday is set yet: 20 years ago. */
export const DEFAULT_AGE = 20;

export function isBirthday(value: unknown): value is string {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [y, m, d] = value.split("-").map(Number);
  const date = new Date(y!, m! - 1, d!);
  return date.getFullYear() === y && date.getMonth() === m! - 1 && date.getDate() === d;
}

/** Whole years on `now` (a birthday later this year doesn't count yet). */
export function ageOn(birthday: string, now = new Date()): number {
  const [y, m, d] = birthday.split("-").map(Number);
  const hadBirthday = now.getMonth() + 1 > m! || (now.getMonth() + 1 === m! && now.getDate() >= d!);
  return now.getFullYear() - y! - (hadBirthday ? 0 : 1);
}

/**
 * The age with a running fraction: whole years, then how far it is from the last birthday to the next one. So it reads
 * exactly 30.000… on the 30th birthday and never jumps when a birthday comes round.
 */
export function exactAge(birthday: string, now = new Date()): number {
  const [y, m, d] = birthday.split("-").map(Number);
  const whole = ageOn(birthday, now);
  const last = new Date(y! + whole, m! - 1, d!).getTime();
  const next = new Date(y! + whole + 1, m! - 1, d!).getTime();
  return whole + (now.getTime() - last) / (next - last);
}

/** "34" and ".58392104": the big number and its running decimals. */
export function ageParts(age: number, digits = 8): { whole: string; fraction: string } {
  const whole = Math.floor(age);
  // Cut, not rounded: rounding would show .00000000 a moment before the birthday, under the old whole number.
  const fraction = String(Math.floor((age - whole) * 10 ** digits)).padStart(digits, "0");
  return { whole: String(whole), fraction: `.${fraction}` };
}

export const birthdayOf = (year: number, month: number, day: number) =>
  `${year}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;

export const daysIn = (year: number, month: number) => new Date(year, month, 0).getDate();

/** "14 March 1990". */
export function birthdayText(birthday: string): string {
  const [y, m, d] = birthday.split("-").map(Number);
  return new Date(y!, m! - 1, d!).toLocaleDateString(undefined, { day: "numeric", month: "long", year: "numeric" });
}
