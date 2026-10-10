/** Pure text for the clock and the greeting: no DOM, so it can be tested on its own. */

/** "09:41", or "9:41" with `hour12` (the AM/PM marker is left out on purpose). */
export function timeText(now: Date, hour12: boolean): string {
  return now
    .toLocaleTimeString([], { hour: hour12 ? "numeric" : "2-digit", minute: "2-digit", hour12 })
    .replace(/\s?[AP]M$/i, "");
}

export const firstName = (fullName: string | undefined | null): string => (fullName ?? "").trim().split(/\s+/)[0] ?? "";

/** "Good morning, Akbar." (or "Good morning." without a name). */
export function greetingText(now: Date, name: string): string {
  const h = now.getHours();
  const part = h < 5 ? "Good night" : h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
  const first = firstName(name);
  return first ? `${part}, ${first}.` : `${part}.`;
}
