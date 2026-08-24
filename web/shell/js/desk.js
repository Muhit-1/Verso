/* ==================================================================
   VERSO — the desk

   The three built-in complications: the dial, tasks, and the player.
   Each talks only to the host module; none of them knows what a
   WebView2 is.

   The DOM is only touched when a value actually changes. On a
   wallpaper that runs for eight hours straight, that is the
   difference between idle and a warm laptop.
================================================================== */

import * as Host from "./host.js";
import * as Theme from "./theme.js";
import * as Regions from "./regions.js";

const MONTHS = ["January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December"];
const DAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

const $ = id => document.getElementById(id);
const pad = n => (n < 10 ? "0" : "") + n;

/* ==================================================================
   REGISTER 1 — clock, date, minute track
================================================================== */

function startDial() {
  const elWeekday = $("weekday");
  const elHHMM = $("hhmm");
  const elSecs = $("secs");
  const elDate = $("dateLine");
  const elWeek = $("weekNo");
  const trackEl = $("minuteTrack");

  const ticks = [];
  for (let i = 0; i < 60; i++) {
    const t = document.createElement("span");
    t.className = "tick" + (i % 5 === 0 ? " major" : "");
    trackEl.appendChild(t);
    ticks.push(t);
  }

  function isoWeek(d) {
    const t = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
    const day = t.getUTCDay() || 7;
    t.setUTCDate(t.getUTCDate() + 4 - day);
    const yearStart = new Date(Date.UTC(t.getUTCFullYear(), 0, 1));
    return Math.ceil(((t - yearStart) / 86400000 + 1) / 7);
  }

  const prev = { hhmm: "", secs: "", day: "", minute: -1 };

  function paint() {
    const d = new Date();
    const hhmm = pad(d.getHours()) + ":" + pad(d.getMinutes());
    const ss = pad(d.getSeconds());

    if (hhmm !== prev.hhmm) { elHHMM.textContent = hhmm; prev.hhmm = hhmm; }
    if (ss !== prev.secs) { elSecs.textContent = ss; prev.secs = ss; }

    const dayKey = d.toDateString();
    if (dayKey !== prev.day) {
      elWeekday.textContent = DAYS[d.getDay()];
      elDate.textContent = `${d.getDate()} ${MONTHS[d.getMonth()]} ${d.getFullYear()}`;
      elWeek.textContent = "Week " + pad(isoWeek(d));
      prev.day = dayKey;
    }

    const minute = d.getMinutes();
    if (minute !== prev.minute) {
      ticks.forEach((el, i) => {
        el.classList.toggle("spent", i < minute);
        el.classList.toggle("now", i === minute);
      });
      prev.minute = minute;
    }
  }

  paint();
  setInterval(paint, 1000);
}

/* ==================================================================
   REGISTER 2 — tasks
================================================================== */

function startTasks() {
  const host = Host.get();
  const listEl = $("taskList");
  const emptyEl = $("taskEmpty");
  const countEl = $("taskCount");
  const inputEl = $("taskInput");

  let tasks = [];
  try { tasks = host.loadTasks() ?? []; } catch { tasks = []; }

  const persist = () => {
    try { host.saveTasks(tasks); } catch { /* host offline; keep in memory */ }
  };

  function render() {
    listEl.textContent = "";

    for (const task of tasks) {
      const li = document.createElement("li");
      li.className = "task" + (task.done ? " done" : "");
      li.tabIndex = 0;
      li.setAttribute("role", "checkbox");
      li.setAttribute("aria-checked", task.done ? "true" : "false");

      const box = document.createElement("span");
      box.className = "box";

      const text = document.createElement("span");
      text.className = "task-text";
      text.textContent = task.text;

      li.append(box, text);

      const toggle = () => {
        task.done = !task.done;
        persist();
        render();
      };

      li.addEventListener("click", toggle);
      li.addEventListener("keydown", ev => {
        if (ev.key === "Enter" || ev.key === " ") {
          ev.preventDefault();
          toggle();
        }
      });

      listEl.appendChild(li);
    }

    countEl.textContent = tasks.filter(t => !t.done).length + " open";
    emptyEl.hidden = tasks.length > 0;
  }

  inputEl.addEventListener("keydown", ev => {
    if (ev.key !== "Enter") return;
    const value = inputEl.value.trim();
    if (!value) return;
    tasks.push({ id: "t" + Date.now(), text: value, done: false });
    inputEl.value = "";
    persist();
    render();
  });

  render();
}

/* ==================================================================
   REGISTER 3 — player
================================================================== */

const PLAY_PATH = "M7 4l13 8-13 8z";
const PAUSE_PATH = "M7 5h3.5v14H7zM13.5 5H17v14h-3.5z";

function startPlayer() {
  const host = Host.get();
  const panelEl = $("playerPanel");
  const artEl = $("art");
  const titleEl = $("trackTitle");
  const artistEl = $("trackArtist");
  const fillEl = $("fill");
  const posEl = $("tPos");
  const durEl = $("tDur");
  const playBtn = $("playBtn");
  const playIcon = $("playIcon");
  const queueEl = $("queue");

  const clock = ms => {
    const total = Math.max(0, Math.floor(ms / 1000));
    return Math.floor(total / 60) + ":" + pad(total % 60);
  };

  let lastTrackKey = "";

  function renderQueue() {
    let items = [];
    try { items = host.getQueue() ?? []; } catch { items = []; }

    queueEl.textContent = "";
    items.forEach((item, i) => {
      const li = document.createElement("li");
      li.className = "qrow";

      const idx = document.createElement("span");
      idx.className = "idx";
      idx.textContent = pad(i + 1);

      const t = document.createElement("span");
      t.className = "qt";
      t.textContent = item.title;

      const a = document.createElement("span");
      a.className = "qa";
      a.textContent = item.artist;

      li.append(idx, t, a);
      queueEl.appendChild(li);
    });
  }

  function paint() {
    let np;
    try { np = host.getNowPlaying(); } catch { return; }
    if (!np) return;

    const key = np.title + "|" + np.artist;
    if (key !== lastTrackKey) {
      titleEl.textContent = np.title || "Nothing playing";
      artistEl.textContent = np.artist || "Start something";
      const a = np.art?.[0] ?? "#2E5F63";
      const b = np.art?.[1] ?? "#7FB7A8";
      artEl.style.background =
        `radial-gradient(120% 120% at 18% 12%, ${b}, ${a} 72%)`;
      lastTrackKey = key;
      renderQueue();
    }

    const pct = np.durationMs ? (np.positionMs / np.durationMs) * 100 : 0;
    fillEl.style.width = Math.min(100, Math.max(0, pct)) + "%";
    posEl.textContent = clock(np.positionMs);
    durEl.textContent = clock(np.durationMs);

    panelEl.classList.toggle("playing", !!np.isPlaying);
    playIcon.firstElementChild.setAttribute("d", np.isPlaying ? PAUSE_PATH : PLAY_PATH);
    playBtn.setAttribute("aria-label", np.isPlaying ? "Pause" : "Play");
  }

  playBtn.addEventListener("click", () => {
    const np = host.getNowPlaying();
    if (np?.isPlaying) host.pause(); else host.play();
    paint();
  });

  $("nextBtn").addEventListener("click", () => { host.next(); paint(); });
  $("prevBtn").addEventListener("click", () => { host.previous(); paint(); });

  if (typeof host.onNowPlayingChanged === "function") {
    host.onNowPlayingChanged(paint);
  }

  paint();
  setInterval(paint, 1000);
}

/* ==================================================================
   THEME SWITCHING

   The only theme UI that exists until the settings window lands.
   Press T to cycle; a toast names what you landed on.
================================================================== */

function startThemeSwitch() {
  const toast = $("themeToast");
  const label = $("themeName");
  let hideTimer = 0;

  function announce(manifest) {
    if (!manifest) return;
    label.textContent = manifest.name ?? manifest.id;
    toast.classList.add("show");
    clearTimeout(hideTimer);
    hideTimer = setTimeout(() => toast.classList.remove("show"), 1600);
  }

  Theme.onChange(announce);

  document.addEventListener("keydown", ev => {
    // Never while someone is typing a task.
    if (ev.target instanceof HTMLInputElement) return;
    if (ev.ctrlKey || ev.altKey || ev.metaKey) return;
    if (ev.key !== "t" && ev.key !== "T") return;
    ev.preventDefault();
    Theme.cycle(ev.shiftKey ? -1 : 1);
  });
}

/* ==================================================================
   START

   Theme first and awaited, so nothing paints against a half-applied
   palette. Then the host, then the widgets.
================================================================== */

/* ==================================================================
   LAYER

   The host tells us, via ?layer=, which part of the desk this window
   is responsible for. See the LAYERS block in css/desk.css for what
   each one means and why overlay mode has to avoid soft edges.
================================================================== */

function applyLayer() {
  const requested = new URLSearchParams(location.search).get("layer");
  const layer = ["wallpaper", "overlay"].includes(requested) ? requested : "all";

  document.body.classList.add("layer-" + layer);
  document.documentElement.dataset.layer = layer;
  return layer;
}

async function start() {
  const layer = applyLayer();

  await Theme.start();
  await Host.connect();

  startDial();
  startTasks();
  startPlayer();
  startThemeSwitch();

  document.body.dataset.host = Host.get().kind;
  document.body.dataset.layer = layer;

  // Last: the host reshapes its window to whatever we report, so the
  // geometry has to be final before the first message goes out.
  if (layer === "overlay") {
    Regions.start();
  }
}

start();
