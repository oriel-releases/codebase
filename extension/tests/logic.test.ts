// Run with `npm test` (Node's own test runner; no extra packages). Only the UI-free logic is tested here.
import { test } from "node:test";
import assert from "node:assert/strict";
import { firstName, greetingText, timeText } from "../src/lib/clock.ts";
import { parseSearch } from "../src/lib/search.ts";
import { accentFor } from "../src/lib/accent.ts";
import { ageOn, ageParts, exactAge, isBirthday } from "../src/lib/age.ts";
import { cleanShortcuts, combine, deleteFolder, makeFolder, takeOut, ungroup, updateFolder, type Shortcut } from "../src/lib/shortcuts.ts";

const at = (hour: number) => new Date(2026, 0, 5, hour, 7);

test("greeting follows the hour and uses the first name", () => {
  assert.equal(greetingText(at(3), "Akbar Jorayev"), "Good night, Akbar.");
  assert.equal(greetingText(at(9), "Akbar"), "Good morning, Akbar.");
  assert.equal(greetingText(at(14), ""), "Good afternoon.");
  assert.equal(greetingText(at(20), "  Ann  Lee "), "Good evening, Ann.");
});

test("first name of nothing is empty", () => {
  assert.equal(firstName(undefined), "");
  assert.equal(firstName("   "), "");
});

test("clock text has no AM/PM marker", () => {
  assert.doesNotMatch(timeText(at(15), true), /[AP]M/i);
  assert.match(timeText(at(15), false), /\d{2}.\d{2}/);
});

test("search: addresses open directly, words are a search, empty is nothing", () => {
  assert.deepEqual(parseSearch("example.com/a"), { kind: "address", url: "https://example.com/a" });
  assert.deepEqual(parseSearch("https://oriel.akbarjon.uz"), { kind: "address", url: "https://oriel.akbarjon.uz" });
  assert.deepEqual(parseSearch("northern lights"), { kind: "query", text: "northern lights" });
  assert.deepEqual(parseSearch("weather"), { kind: "query", text: "weather" });
  assert.equal(parseSearch("   "), null);
});

test("accent only applies to the desktop's own photo and a valid color", () => {
  const photo = { id: "p1", color: "#1A73E8" } as Parameters<typeof accentFor>[0];
  assert.equal(accentFor(photo, true, "p1"), "#1A73E8");
  assert.equal(accentFor(photo, false, "p1"), undefined);
  assert.equal(accentFor(photo, true, "other"), undefined);
  assert.equal(accentFor({ ...photo, color: "red" }, true, "p1"), undefined);
  assert.equal(accentFor({ ...photo, color: undefined }, true, "p1"), undefined);
  // A near-black navy keeps its hue but is lifted so it shows on the dark dialog.
  const lifted = accentFor({ ...photo, color: "#1e2a44" }, true, "p1")!;
  assert.match(lifted, /^#[0-9a-f]{6}$/);
  assert.ok(parseInt(lifted.slice(5, 7), 16) > 150 && parseInt(lifted.slice(5, 7), 16) > parseInt(lifted.slice(1, 3), 16));
});

test("shortcut addresses: bare domains get https, only web links pass", async () => {
  const { normalizeUrl, titleFor } = await import("../src/lib/shortcuts.ts");
  assert.equal(normalizeUrl("github.com"), "https://github.com/");
  assert.equal(normalizeUrl(" https://mail.google.com/mail "), "https://mail.google.com/mail");
  assert.equal(normalizeUrl("http://localhost:3000"), "http://localhost:3000/");
  assert.equal(normalizeUrl("javascript:alert(1)"), null);
  assert.equal(normalizeUrl("not a url"), null);
  assert.equal(normalizeUrl("hello"), null);
  assert.equal(titleFor("https://www.youtube.com/watch"), "Youtube");
  const { sameLink } = await import("../src/lib/shortcuts.ts");
  assert.ok(sameLink("https://github.com/", "http://www.github.com"));
  assert.ok(sameLink("https://site.com/a/#top", "https://site.com/a"));
  assert.ok(!sameLink("https://github.com/", "https://github.com/akbar"));
  assert.ok(!sameLink("https://site.com/?q=1", "https://site.com/?q=2"));
});

test("synced settings leave out Shuffle's counter", async () => {
  const { syncedSettings, defaults } = await import("../src/lib/settings.ts");
  const synced = syncedSettings({ ...defaults, bump: 7 });
  assert.equal("bump" in synced, false);
  assert.equal(synced.clock, defaults.clock);
});

test("settings written by an older version are filled in once", async () => {
  const { lacksSettings, syncedSettings, defaults } = await import("../src/lib/settings.ts");
  const { clockAnimation: _new, ...older } = syncedSettings(defaults); // 1.0.3 had no clockAnimation
  assert.equal(lacksSettings(older), true);
  assert.equal(lacksSettings(syncedSettings(defaults)), false);
  assert.equal(lacksSettings({ ...syncedSettings(defaults), clockAnimation: false }), false); // turned off: kept as is
});

test("age: whole years count only from the birthday on", () => {
  assert.equal(ageOn("1990-03-14", new Date(2026, 2, 13)), 35);
  assert.equal(ageOn("1990-03-14", new Date(2026, 2, 14)), 36);
  assert.equal(ageOn("2000-02-29", new Date(2026, 1, 28)), 25);
});

test("age: the running fraction starts at zero on the birthday and never shows the next year early", () => {
  assert.equal(exactAge("1990-03-14", new Date(2026, 2, 14)), 36);
  const justBefore = exactAge("1990-03-14", new Date(2026, 2, 13, 23, 59, 59, 999));
  assert.ok(justBefore < 36 && justBefore > 35.99);
  assert.deepEqual(ageParts(35.999999999), { whole: "35", fraction: ".99999999" });
  assert.deepEqual(ageParts(24.5), { whole: "24", fraction: ".50000000" });
});

test("age: only real dates are birthdays", () => {
  assert.ok(isBirthday("1990-03-14"));
  assert.ok(!isBirthday("1990-02-30"));
  assert.ok(!isBirthday("14.03.1990"));
  assert.ok(!isBirthday(null));
});

const link = (n: string) => ({ title: n, url: `https://${n}.com/` });

test("folders: a link dropped on a link makes a folder in the target's place; on a folder it joins", () => {
  const list: Shortcut[] = [link("a"), link("b"), link("c")];
  const made = combine(list, 2, 0)!;
  assert.deepEqual(made.list, [{ title: "Folder", items: [link("a"), link("c")] }, link("b")]);
  assert.equal(made.folder, 0);
  const joined = combine(made.list, 1, 0)!;
  assert.deepEqual(joined.list, [{ title: "Folder", items: [link("a"), link("c"), link("b")] }]);
  assert.equal(combine(made.list, 0, 1), null); // a folder doesn't go into anything
});

test("folders: taking out, ungrouping, deleting; an emptied folder goes away", () => {
  const list: Shortcut[] = [link("x"), { title: "F", items: [link("a"), link("b")] }];
  assert.deepEqual(takeOut(list, 1, 0), [link("x"), { title: "F", items: [link("b")] }, link("a")]);
  assert.deepEqual(takeOut([{ title: "F", items: [link("a")] }], 0, 0), [link("a")]);
  assert.deepEqual(updateFolder(list, 1, (f) => ({ ...f, items: [] })), [link("x")]);
  assert.deepEqual(ungroup(list, 1), [link("x"), link("a"), link("b")]);
  assert.deepEqual(deleteFolder(list, 1, true), [link("x"), link("a"), link("b")]);
  assert.deepEqual(deleteFolder(list, 1, false), [link("x")]);
});

test("folders: made from picked links in the first one's place, never empty", () => {
  const list: Shortcut[] = [link("a"), link("b"), link("c")];
  assert.deepEqual(makeFolder(list, "Work", [2, 1]), { list: [link("a"), { title: "Work", items: [link("b"), link("c")] }], folder: 1 });
  assert.equal(makeFolder(list, "Empty", []), null);
});

test("folders: what isn't understood is left out, empty folders too", () => {
  assert.deepEqual(cleanShortcuts([link("a"), { title: "", items: [link("b"), { title: "no url" }] }, { title: "Empty", items: [] }, null, "x"]), [
    link("a"),
    { title: "Folder", items: [link("b")] },
  ]);
});
