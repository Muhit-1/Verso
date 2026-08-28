/* ==================================================================
   VERSO — host bridge

   Everything a widget is allowed to do to the machine goes through
   this module, and nothing else in the web layer ever touches
   window.chrome.webview directly.

   Two implementations satisfy the same shape:

     RealHost  wraps the C# ScriptBridge injected by WebView2
     DemoHost  a self-contained fake, used whenever there is no bridge

   That is why this file opens in a plain browser, in Lively, and in
   the native app with no changes — and why the whole desk can be
   built before the host exists.

   The public API is synchronous on purpose. The COM proxy underneath
   is not: every call across it returns a promise. So RealHost keeps a
   cache, refreshes it in the background, and hands out the last known
   value. A widget repainting once a second must never await anything.
================================================================== */

const TASKS_KEY = "verso.tasks.v1";

/* ---- demo implementation ---------------------------------------- */

function readLocalTasks() {
  try {
    const raw = localStorage.getItem(TASKS_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function writeLocalTasks(tasks) {
  try {
    localStorage.setItem(TASKS_KEY, JSON.stringify(tasks));
  } catch {
    /* private window or storage disabled — run in memory */
  }
}

/**
 * Widget discovery for a plain browser: there is no host to list a
 * directory, so — same trick as theme.js — read a hand-maintained
 * index file and fetch each manifest it names.
 */
async function discoverDemoWidgets() {
  try {
    const res = await fetch("../widgets/widgets.json", { cache: "no-cache" });
    const index = await res.json();

    const found = await Promise.all((index.widgets ?? []).map(async id => {
      try {
        const r = await fetch(`../widgets/${id}/widget.json`, { cache: "no-cache" });
        if (!r.ok) return null;
        const manifest = await r.json();
        // Folder name wins, same rule as themes and as the real catalog.
        return { ...manifest, id, source: "bundled" };
      } catch {
        return null;
      }
    }));

    return found.filter(Boolean);
  } catch {
    return [];
  }
}

async function createDemoHost() {
  const widgets = await discoverDemoWidgets();

  const seeded = [
    { id: "t1", text: "Run the Phase 0 spike", done: true },
    { id: "t2", text: "Decide: mouse hook, or a layered widget window", done: false },
    { id: "t3", text: "Read how Lively forwards input", done: false },
    { id: "t4", text: "Write a second theme, to test the token contract", done: true }
  ];

  const library = [
    { title: "Ricercar",   artist: "Nils Frahm", album: "Solo",   art: ["#2E5F63", "#7FB7A8"], durationMs: 5 * 60000 + 42000 },
    { title: "Ambre",      artist: "Nils Frahm", album: "Felt",   art: ["#4A3E5C", "#9E88B8"], durationMs: 4 * 60000 + 11000 },
    { title: "Says",       artist: "Nils Frahm", album: "Spaces", art: ["#5C4A32", "#C9A96A"], durationMs: 8 * 60000 + 56000 },
    { title: "An Ending",  artist: "Brian Eno",  album: "Apollo", art: ["#22404F", "#6FA3B8"], durationMs: 4 * 60000 + 21000 },
    { title: "Avril 14th", artist: "Aphex Twin", album: "Drukqs", art: ["#3A5240", "#8FBF95"], durationMs: 2 * 60000 + 5000 }
  ];

  let idx = 0;
  let positionMs = 74000;
  let isPlaying = true;
  let last = Date.now();
  const listeners = [];

  const emit = () => listeners.forEach(cb => cb(getNowPlaying()));

  function getNowPlaying() {
    const t = library[idx];
    return {
      title: t.title,
      artist: t.artist,
      album: t.album,
      durationMs: t.durationMs,
      positionMs: Math.min(positionMs, t.durationMs),
      isPlaying,
      art: t.art
    };
  }

  function next() {
    idx = (idx + 1) % library.length;
    positionMs = 0;
    emit();
  }

  function previous() {
    // Same rule every real player uses: rewind first, skip back second.
    if (positionMs > 4000) positionMs = 0;
    else {
      idx = (idx - 1 + library.length) % library.length;
      positionMs = 0;
    }
    emit();
  }

  // Advance the playhead the way a real player would.
  setInterval(() => {
    const now = Date.now();
    const delta = now - last;
    last = now;
    if (!isPlaying) return;
    positionMs += delta;
    if (positionMs >= library[idx].durationMs) next();
  }, 500);

  return {
    kind: "demo",
    hostVersion: "demo",
    loadTasks: () => readLocalTasks() ?? seeded.slice(),
    saveTasks: writeLocalTasks,
    getNowPlaying,
    getQueue: () => [1, 2, 3].map(o => {
      const t = library[(idx + o) % library.length];
      return { title: t.title, artist: t.artist };
    }),
    play() { isPlaying = true; last = Date.now(); emit(); },
    pause() { isPlaying = false; emit(); },
    next,
    previous,
    onNowPlayingChanged: cb => listeners.push(cb),
    // No host, so no chosen background either — the stage falls back to
    // whatever the active theme's own --stage/--photo tokens paint.
    getBackground: () => ({ kind: "none", url: null }),
    // Layout has no demo data: an empty object means "wherever the CSS
    // puts them", which is the correct first-run desk. layout.js falls
    // back to localStorage, so dragging still persists in a browser.
    getLayout: () => null,
    saveLayout: () => {},

    getWidgets: () => widgets,

    // No real apps to launch without a host. Seeded so the app-launcher
    // widget has something to show while being developed standalone.
    getLauncherEntries: () => [
      { id: "demo-1", name: "Notepad" },
      { id: "demo-2", name: "Calculator" }
    ],
    launchApp(id) {
      console.log(`Verso (demo): would launch ${id}`);
    }
  };
}

/* ---- real implementation ---------------------------------------- */

async function createRealHost(bridge) {
  const listeners = [];

  let tasks = [];
  let nowPlaying = null;
  let queue = [];

  const parse = (json, fallback) => {
    try {
      const v = JSON.parse(json);
      return v ?? fallback;
    } catch {
      return fallback;
    }
  };

  async function refreshMedia() {
    const before = nowPlaying ? nowPlaying.title + "|" + nowPlaying.artist : "";
    nowPlaying = parse(await bridge.GetNowPlaying(), null);
    const after = nowPlaying ? nowPlaying.title + "|" + nowPlaying.artist : "";

    if (before !== after) {
      queue = parse(await bridge.GetQueue(), []);
    }
    listeners.forEach(cb => cb(nowPlaying));
  }

  tasks = parse(await bridge.LoadTasks(), []);
  await refreshMedia();

  // Read once, at startup. Unlike media it never changes mid-session — the
  // tray reloads the page after a new background is picked, same as it
  // does after a theme is installed.
  const background = parse(await bridge.GetBackground(), { kind: "none", url: null });

  // Same reasoning as the background: read once at startup, because the
  // only thing that changes it mid-session is this page itself.
  let layout = parse(await bridge.GetLayout(), {});

  // Same again: the set of installed widgets does not change mid-session
  // either — a widget dropped into the folder while Verso is running
  // needs a restart to be picked up, same as a widget.json edit does.
  const widgets = parse(await bridge.GetWidgets(), []);

  // And again: the launcher list only changes from the dashboard, which
  // reloads the desk itself after adding or removing an entry.
  const launcherEntries = parse(await bridge.GetLauncherEntries(), []);

  // Polling, deliberately, even now that Phase 8 wired SMTC up for real.
  // SmtcNowPlayingSource does raise real .NET events on its side, but
  // COM events do not survive the JS boundary cleanly, and one call a
  // second across the proxy costs nothing measurable — simpler than a
  // WebMessage-based push channel for a difference no one would see.
  setInterval(refreshMedia, 1000);

  return {
    kind: "host",
    hostVersion: await bridge.HostVersion(),

    loadTasks: () => tasks,
    saveTasks(next) {
      tasks = next;                                  // cache first, so the
      bridge.SaveTasks(JSON.stringify(next));        // UI never waits on IO
    },

    getNowPlaying: () => nowPlaying,
    getQueue: () => queue,

    play() { bridge.Play(); refreshMedia(); },
    pause() { bridge.Pause(); refreshMedia(); },
    next() { bridge.Next(); refreshMedia(); },
    previous() { bridge.Previous(); refreshMedia(); },

    onNowPlayingChanged: cb => listeners.push(cb),
    getBackground: () => background,

    getLayout: () => layout,
    saveLayout(next) {
      layout = next;                                 // cache first, so a drag
      bridge.SaveLayout(JSON.stringify(next));       // never waits on IO
    },

    getWidgets: () => widgets,

    getLauncherEntries: () => launcherEntries,
    launchApp(id) { bridge.LaunchApp(id); }
  };
}

/* ---- selection -------------------------------------------------- */

let host = null;

/**
 * Resolves the bridge once, at startup. Everything after that calls
 * {@link get}, which is synchronous.
 *
 * A host that exists but throws is treated as no host at all: a widget
 * desk that renders demo data is a far better failure than one that
 * renders nothing.
 */
export async function connect() {
  const bridge = window.chrome?.webview?.hostObjects?.VersoHost;

  if (bridge) {
    try {
      host = await createRealHost(bridge);
      return host;
    } catch (err) {
      console.warn("Verso: host bridge failed, falling back to demo data.", err);
    }
  }

  host = await createDemoHost();
  return host;
}

/**
 * The empty stand-in {@link get} hands out if it is ever called before
 * {@link connect} has resolved. Every module in the shell awaits connect()
 * before touching Host.get(), so this path is not expected to run — but
 * createDemoHost() is async now (it discovers widgets over fetch), so
 * get() can no longer build a demo host on demand the way it used to.
 * Returning something inert is still better than throwing into whatever
 * called it.
 */
const EMPTY_HOST = {
  kind: "demo", hostVersion: "demo",
  loadTasks: () => [], saveTasks() {},
  getNowPlaying: () => null, getQueue: () => [],
  play() {}, pause() {}, next() {}, previous() {}, onNowPlayingChanged() {},
  getBackground: () => ({ kind: "none", url: null }),
  getLayout: () => null, saveLayout() {},
  getWidgets: () => [],
  getLauncherEntries: () => [], launchApp() {}
};

export function get() {
  if (!host) {
    console.warn("Verso: Host.get() called before Host.connect() resolved.");
  }
  return host ?? EMPTY_HOST;
}
