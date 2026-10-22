/**
 * A birthday is kept as "YYYY-MM-DD" in users/{uid}.birthday (firestore.rules checks the shape); the age is worked out
 * from it wherever it shows, so it is always right. The extension has the same logic in extension/src/lib/age.ts.
 */

export const MIN_AGE = 5;
export const MAX_AGE = 120;

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

export const birthdayOf = (year: number, month: number, day: number) =>
  `${year}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;

export const daysIn = (year: number, month: number) => new Date(year, month, 0).getDate();

/** "14 March 1990". */
export function birthdayText(birthday: string): string {
  const [y, m, d] = birthday.split("-").map(Number);
  return new Date(y!, m! - 1, d!).toLocaleDateString("en", { day: "numeric", month: "long", year: "numeric" });
}
