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
