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

function createDemoHost() {
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
    onNowPlayingChanged: cb => listeners.push(cb)
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

  // Until the host raises a real event (Phase 3), poll. One call a second
  // across a COM proxy costs nothing measurable, and it keeps this file
  // from having to change shape when the event arrives.
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

    onNowPlayingChanged: cb => listeners.push(cb)
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

  host = createDemoHost();
  return host;
}

export function get() {
  return host ?? (host = createDemoHost());
}
