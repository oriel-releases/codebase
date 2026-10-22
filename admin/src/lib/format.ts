export const platformNames: Record<string, string> = {
  windows: "Windows",
  macos: "macOS",
  linux: "Linux",
  android: "Android",
  extension: "Extension",
  web: "Website",
};

export const platformOrder = ["windows", "macos", "linux", "android", "extension", "web"];

/** "5 minutes ago", "yesterday", "3 Oct 2026". */
export function timeAgo(date: Date | null, now = Date.now()): string {
  if (!date) return "—";
  const seconds = Math.round((date.getTime() - now) / 1000);
  if (Math.abs(seconds) < 60) return "just now";
  if (Math.abs(seconds) >= 86400 * 30) return shortDate(date);
  const rtf = new Intl.RelativeTimeFormat("en", { numeric: "auto" });
  const steps: [Intl.RelativeTimeFormatUnit, number][] = [["day", 86400], ["hour", 3600], ["minute", 60]];
  const [unit, size] = steps.find(([, s]) => Math.abs(seconds) >= s)!;
  return rtf.format(Math.round(seconds / size), unit);
}

export function shortDate(date: Date | null): string {
  return date ? date.toLocaleDateString("en", { day: "numeric", month: "short", year: "numeric" }) : "—";
}

export function dateTime(date: Date | null): string {
  return date ? date.toLocaleString("en", { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" }) : "—";
}

export const within = (date: Date | null, ms: number, now = Date.now()) => !!date && now - date.getTime() <= ms;

export const DAY = 86_400_000;

export function initials(name: string, email: string): string {
  const source = name.trim() || email;
  return source
    .split(/[\s@.]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toUpperCase())
    .join("");
}

/** "1.1.1" vs "1.0.0"; anything after a "-" is ignored (like the apps' updaters). */
export function compareVersions(a: string, b: string): number {
  const parts = (v: string) => v.split("-")[0]!.split(".").map((p) => parseInt(p, 10) || 0);
  const x = parts(a);
  const y = parts(b);
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    const d = (x[i] ?? 0) - (y[i] ?? 0);
    if (d) return d;
  }
  return 0;
}

/** Whole years from a "YYYY-MM-DD" birthday (users/{uid}.birthday, set on the website's /me). */
export function ageFrom(birthday: string, now = new Date()): number {
  const [y, m, d] = birthday.split("-").map(Number);
  const hadBirthday = now.getMonth() + 1 > m! || (now.getMonth() + 1 === m! && now.getDate() >= d!);
  return now.getFullYear() - y! - (hadBirthday ? 0 : 1);
}

/** "14 Mar 1990". */
export function birthdayText(birthday: string): string {
  const [y, m, d] = birthday.split("-").map(Number);
  return new Date(y!, m! - 1, d!).toLocaleDateString("en", { day: "numeric", month: "short", year: "numeric" });
}
