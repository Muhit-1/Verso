/* ==================================================================
   VERSO — Outpost widgets: clock, theme/12-24/hide controls, now playing + music panel, dock,
   Pomodoro, stopwatch. Plain DOM, no framework: each is a handful of lines and the markup lives in
   index.html (from the approved mockup, design/outpost-desk/).

   Every timer here is deliberate: the clock ticks once a second, the Pomodoro only while running,
   the stopwatch only while running. Nothing animates when nothing is happening.
================================================================== */

import * as Host from "/shell/js/host.js";
import * as Core from "./core.js";
import * as Layout from "./layout.js";

const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
const pad2 = n => String(n).padStart(2, "0");

/* ---- clock ---- */

let is24 = false;

function tickClock() {
  const d = new Date();
  let h = d.getHours();
  const ap = h >= 12 ? "PM" : "AM";
  if (!is24) h = h % 12 || 12;
  setText("#hh", pad2(h));
  setText("#mm", pad2(d.getMinutes()));
  $("#ap").style.display = is24 ? "none" : "";
  setText("#ap", ap);
  setText("#dt", d.toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric" }));
}

/** Writing identical text still invalidates layout; skip it. */
function setText(selector, text) {
  const el = $(selector);
  if (el.textContent !== text) el.textContent = text;
}

function set24(on, save = true) {
  is24 = on;
  $("#b24").classList.toggle("on", on);
  $("#b12").classList.toggle("on", !on);
  tickClock();
  if (save) Core.patchState({ is24: on });
}

/* ---- now playing + music panel ---- */

const fmtTime = ms => {
  const s = Math.max(0, Math.floor(ms / 1000));
  return Math.floor(s / 60) + ":" + pad2(s % 60);
};

let lastNpKey = "";

function renderNowPlaying(np) {
  const text = np && (np.title || np.artist) ? [np.title, np.artist].filter(Boolean).join(" — ") : "Nothing playing";

  // pill
  const mq = $("#npMq");
  const span = $("#npText");
  const key = text;
  if (key !== lastNpKey) {
    lastNpKey = key;
    span.textContent = text;
    mq.classList.remove("scroll");
    // measure after the text is in; scroll only when it does not fit
    requestAnimationFrame(() => {
      if (span.scrollWidth > mq.clientWidth) {
        span.textContent = text + "   ·   " + text + "   ·   ";
        mq.style.setProperty("--mq-dur", Math.max(10, text.length * 0.32) + "s");
        mq.classList.add("scroll");
      }
    });
  }

  // music panel
  setText("#mTitle", np?.title || "Nothing playing");
  setText("#mArtist", np ? np.artist || "" : "Start something in Spotify, YouTube…");
  setText("#mCur", np ? fmtTime(np.positionMs) : "0:00");
  setText("#mDur", np ? fmtTime(np.durationMs) : "0:00");
  const pct = np && np.durationMs > 0 ? Math.min(100, (100 * np.positionMs) / np.durationMs) : 0;
  $("#mBar").style.width = pct + "%";
  if (np?.art?.length >= 2) $("#mArt").style.background = `linear-gradient(135deg, ${np.art[1]}, ${np.art[0]})`;

  $("#mIcon").innerHTML = np?.isPlaying
    ? '<path d="M6 4h4v16H6zM14 4h4v16h-4z"/>'
    : '<path d="M6 4l14 8-14 8z"/>';
  $("#mPlay").dataset.playing = np?.isPlaying ? "1" : "";
}

/* ---- Pomodoro ---- */

const pomo = { f: 25, b: 5, left: 25 * 60, run: false, brk: false, timer: null, sessions: 0 };
const fmtClock = s => pad2(Math.floor(s / 60)) + ":" + pad2(s % 60);

function drawPomo() {
  const total = (pomo.brk ? pomo.b : pomo.f) * 60;
  setText("#pDisp", fmtClock(pomo.left));
  $("#ringFg").style.strokeDashoffset = 263.89 * (1 - pomo.left / total);
  setText("#pSub", pomo.brk ? "Break" : "Focus");
  setText("#pLabel", pomo.brk ? "take five" : "focus session");
  setText("#pStart", pomo.run ? "Pause" : "Start");
  const done = pomo.sessions === 0 ? 0 : ((pomo.sessions - 1) % 4) + 1;
  $$("#pDots i").forEach((dot, i) => dot.classList.toggle("d", i < done));
}

function stepPomo() {
  if (--pomo.left > 0) {
    drawPomo();
    return;
  }
  if (!pomo.brk) pomo.sessions++;
  pomo.brk = !pomo.brk;
  pomo.left = (pomo.brk ? pomo.b : pomo.f) * 60;
  announce(pomo.brk ? "Focus complete" : "Break over");
  drawPomo();
}

function announce(message) {
  const t = $("#toast");
  t.textContent = message;
  t.classList.add("show");
  clearTimeout(announce.h);
  announce.h = setTimeout(() => t.classList.remove("show"), 2600);
}

function resetPomo() {
  pomo.run = false;
  clearInterval(pomo.timer);
  pomo.brk = false;
  pomo.left = pomo.f * 60;
  drawPomo();
}

/* ---- stopwatch ---- */

const sw = { t: 0, run: false, timer: null, last: 0 };
const swHtml = ms => {
  const c = Math.floor(ms / 10) % 100;
  const s = Math.floor(ms / 1000) % 60;
  const m = Math.floor(ms / 60000);
  return `${pad2(m)}:${pad2(s)}<span class="cs">.${pad2(c)}</span>`;
};

/* ---- wiring ---- */

export function start() {
  // clock
  tickClock();
  setInterval(tickClock, 1000);
  $("#b12").addEventListener("click", () => set24(false));
  $("#b24").addEventListener("click", () => set24(true));
  set24(!!Core.getState().is24, false);

  // controls
  $("#bTheme").addEventListener("click", Core.cyclePalette);
  $("#bHide").addEventListener("click", () => document.body.classList.toggle("hide-ui"));

  // dock + panels
  $$("#dock button").forEach(b => b.addEventListener("click", e => {
    e.stopPropagation();
    Layout.openPanel(b.dataset.p);
  }));
  document.addEventListener("click", e => {
    if (!e.target.closest(".panel, .dock, .hint")) Layout.closePanels();
  });

  // now playing
  const host = Host.get();
  host.onNowPlayingChanged(renderNowPlaying);
  renderNowPlaying(host.getNowPlaying());
  $("#mPlay").addEventListener("click", () => ($("#mPlay").dataset.playing ? host.pause() : host.play()));
  $("#mPrev").addEventListener("click", () => host.previous());
  $("#mNext").addEventListener("click", () => host.next());

  // pomodoro
  $("#pStart").addEventListener("click", () => {
    pomo.run = !pomo.run;
    clearInterval(pomo.timer);
    if (pomo.run) pomo.timer = setInterval(stepPomo, 1000);
    drawPomo();
  });
  $("#pReset").addEventListener("click", resetPomo);
  $$("#chips .chip").forEach(c => c.addEventListener("click", () => {
    $$("#chips .chip").forEach(x => x.classList.remove("on"));
    c.classList.add("on");
    pomo.f = +c.dataset.f;
    pomo.b = +c.dataset.b;
    resetPomo();
  }));
  drawPomo();

  // stopwatch
  $("#swStart").addEventListener("click", () => {
    sw.run = !sw.run;
    $("#swStart").textContent = sw.run ? "Stop" : "Start";
    clearInterval(sw.timer);
    if (sw.run) {
      const t0 = performance.now() - sw.t;
      sw.timer = setInterval(() => {
        sw.t = performance.now() - t0;
        $("#swDisp").innerHTML = swHtml(sw.t);
      }, 50);
    }
  });
  $("#swLap").addEventListener("click", () => {
    if (!sw.t) return;
    const row = document.createElement("div");
    row.innerHTML = `<span>Lap ${$("#laps").children.length + 1}</span><span>${swHtml(sw.t - sw.last).replace(/<[^>]+>/g, "")}</span>`;
    sw.last = sw.t;
    $("#laps").prepend(row);
  });
  $("#swReset").addEventListener("click", () => {
    sw.run = false;
    clearInterval(sw.timer);
    sw.t = 0;
    sw.last = 0;
    $("#swStart").textContent = "Start";
    $("#swDisp").innerHTML = swHtml(0);
    $("#laps").innerHTML = "";
  });
}
