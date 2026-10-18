/* ==================================================================
   VERSO DASHBOARD — host bridge

   Wraps window.chrome.webview.hostObjects.VersoDashboard the same way
   web/shell/js/host.js wraps VersoHost — RealHost against the native
   bridge, DemoHost as a self-contained fallback so this page still
   opens in a plain browser. See docs/host-api.md for why VersoDashboard
   is a *different* object from VersoHost: this page reaches privileges
   (enable a widget, hide the taskbar, pick a file) no widget should
   ever be able to reach, even by accident.
================================================================== */

/* ---- demo implementation ---------------------------------------- */

async function fetchIndexed(indexUrl, folderOf) {
  try {
    const res = await fetch(indexUrl, { cache: "no-cache" });
    const index = await res.json();
    const ids = index.themes ?? index.widgets ?? [];
    const found = await Promise.all(ids.map(async id => {
      try {
        const r = await fetch(`${folderOf(id)}`, { cache: "no-cache" });
        if (!r.ok) return null;
        const manifest = await r.json();
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
  const [deskThemes, dashboardThemes, widgets] = await Promise.all([
    fetchIndexed("../themes/themes.json", id => `../themes/${id}/theme.json`),
    fetchIndexed("themes/themes.json", id => `themes/${id}/theme.json`),
    fetchIndexed("../widgets/widgets.json", id => `../widgets/${id}/widget.json`)
  ]);

  let deskThemeId = deskThemes[0]?.id ?? "smoked-petrol";
  let dashboardThemeId = dashboardThemes[0]?.id ?? "fieldnote";
  const disabledWidgets = new Set();
  let background = { kind: "none", url: null };
  const cleanup = { hideTaskbar: false, hideDesktopIcons: false };
  const launcher = [{ id: "demo-1", name: "Notepad" }, { id: "demo-2", name: "Calculator" }];
  const outpostThemes = [
    { id: "sunset", name: "Sunset", from: "#3a2540", to: "#ff9e64" },
    { id: "night", name: "Night", from: "#0a1428", to: "#FFD700" },
    { id: "evening", name: "Evening", from: "#1e1040", to: "#c084fc" }
  ];
  let outpostTheme = "sunset";
  let demoNotes = 1;
  const boxes = [
    { id: "recent", name: "Recent", builtIn: true, apps: [{ id: "d1", name: "Notepad" }, { id: "d2", name: "Calculator" }] },
    { id: "work", name: "Work", builtIn: false, apps: [] }
  ];

  return {
    kind: "demo",
    hostVersion: "demo",

    getThemes: () => deskThemes,
    getThemeId: () => deskThemeId,
    setThemeId(id) { deskThemeId = id; },

    getDashboardThemes: () => dashboardThemes,
    getDashboardThemeId: () => dashboardThemeId,
    setDashboardThemeId(id) { dashboardThemeId = id; },

    getWidgets: () => widgets.map(w => ({ ...w, enabled: !disabledWidgets.has(w.id) })),
    setWidgetEnabled(id, enabled) {
      if (enabled) disabledWidgets.delete(id);
      else disabledWidgets.add(id);
    },
    resetLayout() { console.log("Verso (demo): would reset panel positions."); },

    getBackground: () => background,
    async pickBackground() {
      console.log("Verso (demo): no native file picker in a plain browser.");
      return background;
    },
    clearBackground() { background = { kind: "none", url: null }; },

    getDesktopCleanup: () => cleanup,
    setTaskbarHidden(hide) { cleanup.hideTaskbar = hide; },
    setDesktopIconsHidden(hide) { cleanup.hideDesktopIcons = hide; },

    getDeskStyle: () => "outpost",
    getOutpostThemes: () => outpostThemes.map(t => ({ ...t, selected: t.id === outpostTheme })),
    getOutpostTheme: () => outpostTheme,
    setOutpostTheme(id) { outpostTheme = id; },

    getNoteCount: () => demoNotes,
    newNote() { demoNotes += 1; },

    getApps: () => boxes.map(b => ({ ...b, apps: b.apps.slice() })),
    getAppIcon: () => "",
    async addApp() { console.log("Verso (demo): no native file picker in a plain browser."); },
    removeApp(id) { boxes.forEach(b => { b.apps = b.apps.filter(a => a.id !== id); }); },
    moveApp(id, boxId) {
      let app = null;
      boxes.forEach(b => { const a = b.apps.find(x => x.id === id); if (a) { app = a; b.apps = b.apps.filter(x => x !== a); } });
      (boxes.find(b => b.id === boxId) ?? boxes[0]).apps.push(app);
    },
    addBox() { const id = "b" + Date.now(); boxes.push({ id, name: "New box", builtIn: false, apps: [] }); return id; },
    renameBox(id, name) { const b = boxes.find(x => x.id === id); if (b && name.trim()) b.name = name.trim(); },
    deleteBox(id) {
      const gone = boxes.find(b => b.id === id);
      if (!gone || gone.builtIn) return;
      boxes[0].apps.push(...gone.apps);
      boxes.splice(boxes.indexOf(gone), 1);
    },

    getLauncherEntries: () => launcher,
    async addLauncherEntry() {
      console.log("Verso (demo): no native file picker in a plain browser.");
      return launcher;
    },
    removeLauncherEntry(id) {
      const at = launcher.findIndex(e => e.id === id);
      if (at >= 0) launcher.splice(at, 1);
    }
  };
}

/* ---- real implementation ---------------------------------------- */

/**
 * Every call across the COM proxy returns a promise — see docs/host-api.md.
 * Unlike web/shell/js/host.js, this wrapper does not need to hide that:
 * the dashboard is interaction-driven (click, then re-render), not
 * repainting on a tick budget the way a widget does, so there is no rule
 * against awaiting here.
 */
async function createRealHost(bridge) {
  const iconCache = new Map();   // app id -> data URL (an icon never changes for an id)
  const parse = (json, fallback) => {
    try {
      const v = JSON.parse(json);
      return v ?? fallback;
    } catch {
      return fallback;
    }
  };

  return {
    kind: "host",
    hostVersion: await bridge.HostVersion(),

    getThemes: async () => parse(await bridge.GetThemes(), []),
    getThemeId: () => bridge.GetThemeId(),
    setThemeId: id => bridge.SetThemeId(id),

    getDashboardThemes: async () => parse(await bridge.GetDashboardThemes(), []),
    getDashboardThemeId: () => bridge.GetDashboardThemeId(),
    setDashboardThemeId: id => bridge.SetDashboardThemeId(id),

    getWidgets: async () => parse(await bridge.GetWidgets(), []),
    setWidgetEnabled: (id, enabled) => bridge.SetWidgetEnabled(id, enabled),
    resetLayout: () => bridge.ResetLayout(),

    getBackground: async () => parse(await bridge.GetBackground(), { kind: "none", url: null }),
    async pickBackground() {
      await bridge.PickBackground();
      return parse(await bridge.GetBackground(), { kind: "none", url: null });
    },
    clearBackground: () => bridge.ClearBackground(),

    getDesktopCleanup: async () => parse(await bridge.GetDesktopCleanup(), { hideTaskbar: false, hideDesktopIcons: false }),
    setTaskbarHidden: hide => bridge.SetTaskbarHidden(hide),
    setDesktopIconsHidden: hide => bridge.SetDesktopIconsHidden(hide),

    getDeskStyle: async () => (await bridge.GetDeskStyle()) || "outpost",
    getOutpostThemes: async () => parse(await bridge.GetOutpostThemes(), []),
    getOutpostTheme: async () => (await bridge.GetOutpostTheme()) || "",
    setOutpostTheme: id => bridge.SetOutpostTheme(id),

    getNoteCount: async () => Number(await bridge.GetNoteCount()) || 0,
    newNote: () => bridge.NewNote(),

    getApps: async () => parse(await bridge.GetApps(), []),
    async getAppIcon(id) {
      if (!iconCache.has(id)) iconCache.set(id, (await bridge.GetAppIcon(id)) || "");
      return iconCache.get(id);
    },
    addApp: boxId => bridge.AddApp(boxId || "recent"),
    removeApp: id => bridge.RemoveApp(id),
    moveApp: (id, boxId) => bridge.MoveApp(id, boxId),
    addBox: () => bridge.AddBox(),
    renameBox: (id, name) => bridge.RenameBox(id, name),
    deleteBox: id => bridge.DeleteBox(id),

    getLauncherEntries: async () => parse(await bridge.GetLauncherEntries(), []),
    async addLauncherEntry() {
      await bridge.AddLauncherEntry();
      return parse(await bridge.GetLauncherEntries(), []);
    },
    removeLauncherEntry: id => bridge.RemoveLauncherEntry(id)
  };
}

/* ---- selection -------------------------------------------------- */

let host = null;

export async function connect() {
  const bridge = window.chrome?.webview?.hostObjects?.VersoDashboard;

  if (bridge) {
    try {
      host = await createRealHost(bridge);
      return host;
    } catch (err) {
      console.warn("Verso dashboard: bridge failed, falling back to demo data.", err);
    }
  }

  host = await createDemoHost();
  return host;
}

export function get() {
  if (!host) {
    console.warn("Verso dashboard: Host.get() called before Host.connect() resolved.");
  }
  return host;
}
