// Runs before the new tab's first paint (a plain script at the end of <body>; the page's module only runs after the
// browser has already painted the HTML). It hides what the settings turned off, so the gear, the shuffle button or the
// search bar don't flash on reload, and it puts back the light/dark tones from the last visit (lib/tone.ts saves them)
// so text and buttons don't start white over a bright photo. main.ts takes over from here.
(function () {
  function read(key, fallback) {
    try {
      return JSON.parse(localStorage.getItem(key) || "null") || fallback;
    } catch (e) {
      return fallback;
    }
  }

  // The same defaults as lib/settings.ts for these three.
  var settings = read("oriel.settings", {});
  var hide = function (id, off) {
    var el = document.getElementById(id);
    if (el && off) el.hidden = true;
  };
  hide("open-panel", settings.settingsButton === false);
  hide("shuffle", settings.shuffle === false);
  var search = document.getElementById("search");
  if (search && settings.search !== true) search.style.display = "none";

  var tones = read("oriel.tones", []);
  document.querySelectorAll("[data-adapt]").forEach(function (el, i) {
    if (tones[i] === "dark" || tones[i] === "light") el.dataset.tone = tones[i];
  });
})();
