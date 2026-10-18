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
const NOTES_KEY = "verso.notes.v1";
const AGENDA_KEY = "verso.agenda.v1";

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

function readLocalNotes() {
  try {
    const raw = localStorage.getItem(NOTES_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function writeLocalNotes(notes) {
  try {
    localStorage.setItem(NOTES_KEY, JSON.stringify(notes));
  } catch {
    /* private window or storage disabled — run in memory */
  }
}

function readLocalAgenda() {
  try {
    const raw = localStorage.getItem(AGENDA_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function writeLocalAgenda(items) {
  try {
    localStorage.setItem(AGENDA_KEY, JSON.stringify(items));
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

  const seededNotes = [
    { id: "n1", text: "Ring the ticket desk about the printer" },
    { id: "n2", text: "Half caf, oat milk" }
  ];

  const seededAgenda = [
    { id: "a1", title: "Stand-up", start: "09:00", end: "09:15" },
    { id: "a2", title: "Design review", start: "11:00", end: "12:00" },
    { id: "a3", title: "Focus block", start: "14:00", end: "16:00" }
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
    loadNotes: () => readLocalNotes() ?? seededNotes.slice(),
    saveNotes: writeLocalNotes,
    loadAgenda: () => readLocalAgenda() ?? seededAgenda.slice(),
    saveAgenda: writeLocalAgenda,
    getNowPlaying,
    getQueue: () => [1, 2, 3].map(o => {
      const t = library[(idx + o) % library.length];
      return { title: t.title, artist: t.artist };
    }),
    play() { isPlaying = true; last = Date.now(); emit(); },
    pause() { isPlaying = false; emit(); },
    next,
    previous,
    onNowPlayingChanged: cb => {
      listeners.push(cb);
      return () => { const i = listeners.indexOf(cb); if (i >= 0) listeners.splice(i, 1); };   // a widget that goes away stops listening
    },
    // No host, so no chosen background either — the stage falls back to
    // whatever the active theme's own --stage/--photo tokens paint.
    getBackground: () => ({ kind: "none", url: null }),
    // Layout has no demo data: an empty object means "wherever the CSS
    // puts them", which is the correct first-run desk. layout.js falls
    // back to localStorage, so dragging still persists in a browser.
    getLayout: () => null,
    saveLayout: () => {},

    // The Outpost desk's state; localStorage is enough to develop it in a browser.
    // Outpost widgets: each on/off by itself, defaults like the real host (Clock + Now playing on).
    getOutpostWidgets: () => {
      let map = {};
      try { map = JSON.parse(localStorage.getItem("verso.outpost.widgets") || "{}"); } catch { /* ignore */ }
      return OUTPOST_WIDGETS.map(w => ({ ...w, enabled: map[w.id] ?? w.defaultEnabled }));
    },
    setOutpostWidgetEnabled: (id, on) => {
      let map = {};
      try { map = JSON.parse(localStorage.getItem("verso.outpost.widgets") || "{}"); } catch { /* ignore */ }
      map[id] = on;
      try { localStorage.setItem("verso.outpost.widgets", JSON.stringify(map)); } catch { /* ignore */ }
    },
    openDashboard: () => console.log("Verso (demo): would open the dashboard"),

    // Palette: owned by the host in the real app; kept in memory here.
    getOutpostThemes: () => OUTPOST_THEMES.map(t => ({ ...t, selected: t.id === demoTheme })),
    getOutpostTheme: () => demoTheme,
    setOutpostTheme: id => { demoTheme = id; },

    // App library: boxes with apps, in memory.
    getApps: () => demoBoxes.map(b => ({ ...b, apps: b.apps.slice() })),
    getAppIcon: () => "",
    addApp: () => console.log("Verso (demo): no native file picker in a plain browser."),
    removeApp: id => { demoBoxes.forEach(b => { b.apps = b.apps.filter(a => a.id !== id); }); },
    moveApp: (id, boxId) => {
      let app = null;
      demoBoxes.forEach(b => { const a = b.apps.find(x => x.id === id); if (a) { app = a; b.apps = b.apps.filter(x => x !== a); } });
      const to = demoBoxes.find(b => b.id === boxId) ?? demoBoxes[0];
      if (app) to.apps.push(app);
    },
    addBox: () => {
      const id = "b" + Date.now();
      demoBoxes.push({ id, name: "New box", builtIn: false, apps: [] });
      return id;
    },
    deleteBox: id => {
      const gone = demoBoxes.find(b => b.id === id);
      if (!gone || gone.builtIn) return;
      demoBoxes[0].apps.push(...gone.apps);
      demoBoxes.splice(demoBoxes.indexOf(gone), 1);
    },

    getDeskState: () => {
      try { return JSON.parse(localStorage.getItem("verso.outpost.v1") || "{}"); } catch { return {}; }
    },
    saveDeskState: next => {
      try { localStorage.setItem("verso.outpost.v1", JSON.stringify(next)); } catch { /* private mode */ }
    },

    getWidgets: () => widgets,

    // No real apps to launch without a host. Seeded so the app-launcher
    // widget has something to show while being developed standalone.
    // Sticky notes: in memory here; the real host keeps them in outpost-notes.json.
    getNotes: () => demoNotes.map(n => ({ ...n })),
    newNote: () => {
      const id = "n" + Date.now();
      demoNotes.push({ id, color: NOTE_COLORS[demoNotes.length % NOTE_COLORS.length], html: "" });
      return id;
    },
    setNoteColor: (id, color) => { const n = demoNotes.find(x => x.id === id); if (n) n.color = color; },
    deleteNote: id => { const i = demoNotes.findIndex(x => x.id === id); if (i >= 0) demoNotes.splice(i, 1); },
    editNote: () => console.log("Verso (demo): the pop-up editor needs the real host."),
    editBoxName: () => console.log("Verso (demo): the pop-up editor needs the real host."),

    getLauncherEntries: () => [
      { id: "demo-1", name: "Notepad" },
      { id: "demo-2", name: "Calculator" }
    ],
    launchApp(id) {
      console.log(`Verso (demo): would launch ${id}`);
    }
  };
}

/* The Outpost widgets as the demo host knows them. The real host's list lives in OutpostWidgetCatalog.cs. */
const OUTPOST_WIDGETS = [
  { id: "clock", name: "Clock", description: "The big clock and date.", defaultEnabled: true },
  { id: "np", name: "Now playing", description: "What Windows is playing.", defaultEnabled: true },
  { id: "pomo", name: "Pomodoro", description: "A focus timer.", defaultEnabled: false },
  { id: "sw", name: "Stopwatch", description: "A stopwatch with laps.", defaultEnabled: false },
  { id: "notes", name: "Sticky notes", description: "Notes you can place anywhere.", defaultEnabled: false },
  { id: "launcher", name: "App launcher", description: "See-through boxes of apps.", defaultEnabled: false },
  { id: "flies", name: "Fireflies", description: "Drifting lights.", defaultEnabled: false }
];

const OUTPOST_THEMES = [
  { id: "sunset", name: "Sunset", from: "#3a2540", to: "#ff9e64" },
  { id: "night", name: "Night", from: "#0a1428", to: "#FFD700" },
  { id: "evening", name: "Evening", from: "#1e1040", to: "#c084fc" },
  { id: "forest-green", name: "Forest green", from: "#0c2a1e", to: "#66cc99" },
  { id: "black-sunset", name: "Black sunset", from: "#050505", to: "#ff7a00" },
  { id: "pencil", name: "Pencil", from: "#141414", to: "#bbbbbb" }
];
let demoTheme = "";
const NOTE_COLORS = ["#7ac55c", "#5aa9e6", "#f2c94c", "#f08fb0", "#b48ae0", "#a8a8a8"];
const demoNotes = [{ id: "n1", color: NOTE_COLORS[2], html: "Ring the ticket desk<br>about the printer" }];
const demoBoxes = [
  { id: "recent", name: "Recent", builtIn: true, apps: [{ id: "d1", name: "Notepad" }, { id: "d2", name: "Calculator" }] },
  { id: "work", name: "Work", builtIn: false, apps: [] }
];

/* ---- real implementation ---------------------------------------- */

async function createRealHost(bridge) {
  const listeners = [];
  const iconCache = new Map();   // app id -> data URL (an icon never changes for an id)

  let tasks = [];
  let notes = [];
  let agenda = [];
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
  notes = parse(await bridge.LoadNotes(), []);
  agenda = parse(await bridge.LoadAgenda(), []);
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

  // The Outpost desk's own state (element positions, palette, 12/24h). Opaque to the host.
  let deskState = parse(await bridge.GetDeskState(), {});

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

    loadNotes: () => notes,
    saveNotes(next) {
      notes = next;
      bridge.SaveNotes(JSON.stringify(next));
    },

    loadAgenda: () => agenda,
    saveAgenda(next) {
      agenda = next;
      bridge.SaveAgenda(JSON.stringify(next));
    },

    getNowPlaying: () => nowPlaying,
    getQueue: () => queue,

    play() { bridge.Play(); refreshMedia(); },
    pause() { bridge.Pause(); refreshMedia(); },
    next() { bridge.Next(); refreshMedia(); },
    previous() { bridge.Previous(); refreshMedia(); },

    onNowPlayingChanged: cb => {
      listeners.push(cb);
      return () => { const i = listeners.indexOf(cb); if (i >= 0) listeners.splice(i, 1); };   // a widget that goes away stops listening
    },
    getBackground: () => background,

    getLayout: () => layout,
    saveLayout(next) {
      layout = next;                                 // cache first, so a drag
      bridge.SaveLayout(JSON.stringify(next));       // never waits on IO
    },

    getWidgets: () => widgets,

    getDeskState: () => deskState,
    saveDeskState(next) {
      deskState = next;                              // cache first, like layout
      bridge.SaveDeskState(JSON.stringify(next));
    },

    // Outpost widgets are read fresh every time (they change live from the dashboard and the desk menu).
    getOutpostWidgets: async () => parse(await bridge.GetOutpostWidgets(), []),
    setOutpostWidgetEnabled: (id, on) => bridge.SetOutpostWidgetEnabled(id, on),
    openDashboard: () => bridge.OpenDashboard(),

    // Palette and app library are owned by the host; read fresh, so the desk always agrees with the dashboard.
    getOutpostThemes: async () => parse(await bridge.GetOutpostThemes(), []),
    getOutpostTheme: async () => (await bridge.GetOutpostTheme()) || "",
    setOutpostTheme: id => bridge.SetOutpostTheme(id),

    getApps: async () => parse(await bridge.GetApps(), []),
    async getAppIcon(id) {
      if (!iconCache.has(id)) iconCache.set(id, (await bridge.GetAppIcon(id)) || "");
      return iconCache.get(id);
    },
    // Sticky notes live in the host (their own file); the desk reads them fresh and the pop-up editor edits them.
    getNotes: async () => parse(await bridge.GetNotes(), []),
    newNote: () => bridge.NewNote(),
    setNoteColor: (id, color) => bridge.SetNoteColor(id, color),
    deleteNote: id => bridge.DeleteNote(id),
    editNote: (id, screenRect) => bridge.EditNote(id, JSON.stringify(screenRect)),
    editBoxName: (id, screenRect) => bridge.EditBoxName(id, JSON.stringify(screenRect)),

    addApp: boxId => bridge.AddApp(boxId),
    removeApp: id => bridge.RemoveApp(id),
    moveApp: (id, boxId) => bridge.MoveApp(id, boxId),
    addBox: () => bridge.AddBox(),
    deleteBox: id => bridge.DeleteBox(id),

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
  loadNotes: () => [], saveNotes() {},
  loadAgenda: () => [], saveAgenda() {},
  getNowPlaying: () => null, getQueue: () => [],
  play() {}, pause() {}, next() {}, previous() {}, onNowPlayingChanged() {},
  getBackground: () => ({ kind: "none", url: null }),
  getLayout: () => null, saveLayout() {},
  getDeskState: () => ({}), saveDeskState() {},
  getOutpostWidgets: () => [], setOutpostWidgetEnabled() {}, openDashboard() {},
  getOutpostThemes: () => [], getOutpostTheme: () => "", setOutpostTheme() {},
  getNotes: () => [], newNote() {}, setNoteColor() {}, deleteNote() {}, editNote() {}, editBoxName() {},
  getApps: () => [], getAppIcon: () => "", addApp() {}, removeApp() {}, moveApp() {}, addBox() {}, deleteBox() {},
  getWidgets: () => [],
  getLauncherEntries: () => [], launchApp() {}
};

export function get() {
  if (!host) {
    console.warn("Verso: Host.get() called before Host.connect() resolved.");
  }
  return host ?? EMPTY_HOST;
}
