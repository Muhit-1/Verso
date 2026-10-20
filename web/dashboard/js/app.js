/* ==================================================================
   VERSO DASHBOARD — bootstrap

   Theme first and awaited, then the host, then every section is
   rendered from real data. A widget card shows an icon for what the
   widget is (never a fake rendering of it); a theme card shows the
   theme's own swatch colours from its theme.json.
================================================================== */

import * as Theme from "./theme.js";
import * as Host from "./host.js";
import * as Titlebar from "./titlebar.js";

const $ = id => document.getElementById(id);

function escapeHtml(s) {
  return String(s ?? "").replace(/[&<>"']/g, c => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
  })[c]);
}

function initials(name) {
  return String(name ?? "?").trim().slice(0, 2).toUpperCase() || "?";
}

function setSwitch(el, on) {
  el.classList.toggle("on", !!on);
  el.setAttribute("role", "switch");
  el.setAttribute("aria-checked", on ? "true" : "false");
}

/** One simple line icon per widget id (Outpost and classic widgets); unknown ids fall back to initials. */
const GRID4 = '<rect x="4" y="4" width="6.5" height="6.5" rx="1.4"/><rect x="13.5" y="4" width="6.5" height="6.5" rx="1.4"/><rect x="4" y="13.5" width="6.5" height="6.5" rx="1.4"/><rect x="13.5" y="13.5" width="6.5" height="6.5" rx="1.4"/>';
const CLOCK = '<circle cx="12" cy="12" r="8.5"/><path d="M12 7v5l3.5 2"/>';
const WIDGET_ICONS = {
  clock: CLOCK,
  "digital-clock": CLOCK,
  np: '<path d="M9 18V6l10-2v12"/><circle cx="6.5" cy="18" r="2.5"/><circle cx="16.5" cy="16" r="2.5"/>',
  player: '<circle cx="12" cy="12" r="8.5"/><path d="M10 8.5v7l6-3.5z"/>',
  pomo: '<circle cx="12" cy="13.5" r="7"/><path d="M12 6.5V4M9.5 4.5l2.5 2 2.5-2"/><path d="M12 10v3.5l2 1.5"/>',
  "focus-timer": '<path d="M7 3h10M7 21h10M8 3c0 5 8 5 8 9s-8 4-8 9M16 3c0 5-8 5-8 9s8 4 8 9"/>',
  sw: '<circle cx="12" cy="13.5" r="7"/><path d="M10 3h4M12 13.5l3-3M18.5 6.5l1.2 1.2"/>',
  flies: '<path d="M12 3l1.6 4.4L18 9l-4.4 1.6L12 15l-1.6-4.4L6 9l4.4-1.6zM18.5 15l.8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8z"/>',
  notes: '<path d="M5 4h14v11l-5 5H5z"/><path d="M14 20v-5h5"/><path d="M8 9h8M8 12.5h5"/>',
  launcher: GRID4,
  "app-launcher": GRID4,
  agenda: '<rect x="4" y="5.5" width="16" height="14.5" rx="2"/><path d="M4 10h16M8.5 3.5v4M15.5 3.5v4"/>',
  dial: '<circle cx="12" cy="12" r="8.5"/><path d="M12 12l4-4M12 5v1.5M5 12h1.5M19 12h-1.5"/>',
  tasks: '<path d="M5 7l1.8 1.8L10 5.5M5 14l1.8 1.8L10 12.5"/><path d="M13 7.5h7M13 14.5h7"/>'
};

function widgetIcon(id, name) {
  const body = WIDGET_ICONS[id];
  return body
    ? `<svg class="card-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${body}</svg>`
    : escapeHtml(initials(name));
}

/* ---- nav ---------------------------------------------------------- */

function startNav() {
  document.querySelectorAll("[data-target]").forEach(btn => {
    btn.addEventListener("click", () => {
      document.querySelectorAll("[data-target]").forEach(b => b.classList.remove("active"));
      document.querySelectorAll(".view").forEach(v => v.classList.remove("active"));
      btn.classList.add("active");
      document.querySelector(`.view[data-view="${btn.dataset.target}"]`).classList.add("active");
    });
  });
}

/* ---- wallpaper ------------------------------------------------------ */

const BACKGROUND_LABELS = { video: "Video", gif: "Animated GIF", interactive: "Interactive" };

function describeBackground(bg) {
  if (!bg || bg.kind === "none") return "None set — the desk's own colours show.";
  if (bg.kind === "image") return "Image — applied to the desktop wallpaper directly.";
  const name = bg.url ? decodeURIComponent(bg.url.split("/").pop()) : "";
  return `${BACKGROUND_LABELS[bg.kind] ?? bg.kind}: ${name}`;
}

async function renderWallpaper() {
  const bg = await Host.get().getBackground();
  $("backgroundText").textContent = describeBackground(bg);
}

function startWallpaper() {
  $("pickBackgroundBtn").addEventListener("click", async () => {
    const bg = await Host.get().pickBackground();
    $("backgroundText").textContent = describeBackground(bg);
  });
  $("clearBackgroundBtn").addEventListener("click", async () => {
    await Host.get().clearBackground();
    renderWallpaper();
  });
}

/* ---- widgets ---------------------------------------------------------- */

/** Number of widget switches whose host call has not finished; the page does not refresh itself meanwhile. */
let toggling = 0;

/** The Sticky notes card: notes are typed on the desk; here you can only add one and see how many there are. */
function addNotesControls(card) {
  const extra = document.createElement("div");
  extra.className = "card-extra";
  extra.innerHTML = `<button type="button" class="btn small" data-new-note>+ New note</button><span class="card-sub" data-note-count></span>`;
  const count = extra.querySelector("[data-note-count]");
  const refresh = async () => {
    const n = await Host.get().getNoteCount();
    count.textContent = `${n} note${n === 1 ? "" : "s"}`;
  };
  extra.querySelector("[data-new-note]").addEventListener("click", async () => {
    toggling++;
    try {
      await Host.get().newNote();   // also switches the widget on
    } finally {
      toggling--;
    }
    renderWidgets();
  });
  card.appendChild(extra);
  refresh();
}

async function renderWidgets() {
  const widgets = await Host.get().getWidgets();
  const grid = $("widgetsGrid");
  grid.innerHTML = "";

  for (const w of widgets) {
    const card = document.createElement("div");
    card.className = "card";
    card.innerHTML = `
      <div class="card-preview">${widgetIcon(w.id, w.name)}</div>
      <div class="card-foot">
        <div>
          <div class="card-name">${escapeHtml(w.name)}</div>
          <div class="card-sub">${escapeHtml(w.description ?? "")}</div>
        </div>
        <button type="button" class="switch${w.enabled ? " on" : ""}" role="switch" aria-checked="${w.enabled ? "true" : "false"}" data-widget-id="${escapeHtml(w.id)}" aria-label="${escapeHtml(w.name)}">
          <div class="knob"></div>
        </button>
      </div>`;
    if (w.id === "notes") addNotesControls(card);
    grid.appendChild(card);
  }

  grid.querySelectorAll("[data-widget-id]").forEach(btn => {
    btn.addEventListener("click", async () => {
      const enabled = !btn.classList.contains("on");
      setSwitch(btn, enabled);
      toggling++;
      try {
        await Host.get().setWidgetEnabled(btn.dataset.widgetId, enabled);
      } finally {
        toggling--;
      }
    });
  });
}

function startWidgets() {
  $("resetLayoutBtn").addEventListener("click", () => Host.get().resetLayout());

  // A widget can also be switched from the desk's round menu; while this page is showing, follow it.
  let last = "";
  setInterval(async () => {
    const view = document.querySelector('[data-view="widgets"]');
    if (!view?.classList.contains("active") || toggling > 0) return;
    const now = JSON.stringify(await Host.get().getWidgets());
    if (now !== last) {
      last = now;
      renderWidgets();
    }
  }, 1500);
}

/* ---- apps (the app library: boxes of apps) ------------------------------------- */

let appsSig = "";

function appIcon(app) {
  const el = document.createElement("div");
  el.className = "app-ico";
  el.textContent = (app.name || "?").trim().slice(0, 1).toUpperCase();
  Promise.resolve(Host.get().getAppIcon(app.id)).then(url => {
    if (!url || !el.isConnected) return;
    el.textContent = "";
    const img = document.createElement("img");
    img.src = url;
    el.appendChild(img);
  }).catch(() => {});
  return el;
}

async function renderApps(force = false) {
  const boxes = await Host.get().getApps();
  const sig = JSON.stringify(boxes);
  if (!force && sig === appsSig) return;
  appsSig = sig;

  const total = boxes.reduce((n, b) => n + b.apps.length, 0);
  $("appsHint").textContent = total
    ? `${total} app${total === 1 ? "" : "s"} in ${boxes.length} box${boxes.length === 1 ? "" : "es"}.`
    : "No apps yet. Add one — it lands in Recent.";

  const root = $("appsBoxes");
  root.innerHTML = "";

  for (const box of boxes) {
    const section = document.createElement("div");
    section.className = "app-box";
    section.dataset.box = box.id;

    const head = document.createElement("div");
    head.className = "app-box-head";
    head.innerHTML = box.builtIn
      ? `<span class="app-box-name">${escapeHtml(box.name)}</span>`
      : `<input class="app-box-name" value="${escapeHtml(box.name)}" maxlength="40" aria-label="Box name" spellcheck="false">`;
    const count = document.createElement("span");
    count.className = "app-box-count";
    count.textContent = `${box.apps.length} app${box.apps.length === 1 ? "" : "s"}${box.builtIn ? " · newest first" : ""}`;
    head.appendChild(count);

    const actions = document.createElement("div");
    actions.className = "app-box-actions";
    actions.innerHTML = `<button type="button" class="btn small" data-add>Add app here</button>`
      + (box.builtIn ? "" : `<button type="button" class="btn small" data-del>Delete box</button>`);
    head.appendChild(actions);
    section.appendChild(head);

    if (!box.apps.length) {
      const none = document.createElement("div");
      none.className = "list-empty";
      none.textContent = "No apps here yet.";
      section.appendChild(none);
    }

    for (const app of box.apps) {
      const row = document.createElement("div");
      row.className = "list-row";
      row.dataset.app = app.id;

      const left = document.createElement("div");
      left.className = "app-row-left";
      left.appendChild(appIcon(app));
      const name = document.createElement("span");
      name.className = "name";
      name.textContent = app.name;
      left.appendChild(name);
      if (app.missing) {
        const gone = document.createElement("span");
        gone.className = "tag-missing";
        gone.textContent = "file not found";
        gone.title = "Its file was moved or deleted, so it cannot open. Remove it and add it again.";
        left.appendChild(gone);
      }
      row.appendChild(left);

      const right = document.createElement("div");
      right.className = "app-row-right";
      const select = document.createElement("select");
      select.className = "select";
      select.setAttribute("aria-label", `Move ${app.name} to a box`);
      select.innerHTML = boxes.map(b => `<option value="${escapeHtml(b.id)}"${b.id === box.id ? " selected" : ""}>Move to: ${escapeHtml(b.name)}</option>`).join("");
      select.addEventListener("change", async () => {
        await Host.get().moveApp(app.id, select.value);
        renderApps(true);
      });
      right.appendChild(select);

      const remove = document.createElement("button");
      remove.type = "button";
      remove.className = "icon-btn";
      remove.setAttribute("aria-label", `Remove ${app.name}`);
      remove.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 6l12 12M18 6L6 18"/></svg>';
      remove.addEventListener("click", async () => {
        await Host.get().removeApp(app.id);
        renderApps(true);
      });
      right.appendChild(remove);
      row.appendChild(right);
      section.appendChild(row);
    }

    section.querySelector("[data-add]").addEventListener("click", () => Host.get().openAppPicker(box.id));
    section.querySelector("[data-del]")?.addEventListener("click", async () => {
      await Host.get().deleteBox(box.id);
      renderApps(true);
    });
    const rename = section.querySelector("input.app-box-name");
    rename?.addEventListener("change", async () => {
      await Host.get().renameBox(box.id, rename.value);
      renderApps(true);
    });
    rename?.addEventListener("keydown", e => { if (e.key === "Enter") rename.blur(); });

    root.appendChild(section);
  }
}

function startApps() {
  // the picker is its own window; the page below picks the new apps up through its normal refresh
  $("addAppBtn").addEventListener("click", () => Host.get().openAppPicker("recent"));
  $("newBoxBtn").addEventListener("click", async () => {
    await Host.get().addBox();
    renderApps(true);
  });

  // Apps can also be added, moved or removed on the desk; while this page is showing, follow along
  // (but never rebuild the page under a box name or a drop-down the user is using).
  setInterval(() => {
    const view = document.querySelector('[data-view="apps"]');
    if (!view?.classList.contains("active")) return;
    if ($("appsBoxes").contains(document.activeElement)) return;
    renderApps();
  }, 1500);
}

/* ---- desktop cleanup ------------------------------------------------------- */

async function renderDesktop() {
  const cleanup = await Host.get().getDesktopCleanup();
  setSwitch($("hideTaskbarSwitch"), cleanup.hideTaskbar);
  setSwitch($("hideIconsSwitch"), cleanup.hideDesktopIcons);
  renderTaskbar();
  renderDock();
}

/* ---- Windows taskbar options ----------------------------------------------- */

const TB_SWITCHES = ["auto", "badges", "flash", "share", "corner", "search", "taskview"];
const TB_NAMES = {
  position: "Position", align: "Alignment", size: "Size", auto: "Auto-hide", badges: "Badges", flash: "Flashing",
  share: "Share window", corner: "Show-desktop corner", combine: "Combine buttons", search: "Search", taskview: "Task view"
};
let tbBusy = false;
let tbUndoState = null;
let tbToastTimer = 0;

async function renderTaskbar() {
  if (tbBusy) return;
  const host = Host.get();
  const s = await host.getTaskbar();
  if (!s) { $("tbOpts").classList.add("dis"); return; }
  applyTaskbarState(s);
  $("tbRestore").disabled = !(await host.hasTaskbarBackup());
}

function applyTaskbarState(s) {
  document.querySelectorAll("#tbPos .pos").forEach(p => p.classList.toggle("on", Number(p.dataset.v) === s.position));
  for (const k of TB_SWITCHES) setSwitch(document.querySelector(`[data-tb="${k}"]`), s[k]);
  const set = (k, v) => { const el = document.querySelector(`select[data-tb="${k}"]`); if (el && document.activeElement !== el) el.value = String(v); };
  set("align", s.align);
  set("size", s.small ? 1 : 0);
  set("combine", s.combine);
  const bottom = s.position === 3;
  $("tbAutoRow").classList.toggle("dis", !bottom);
  $("tbAutoNote").hidden = bottom;
}

/* ---- compact dock ---------------------------------------------------------- */

const DOCK_SWITCHES = ["enabled", "showStart", "peek", "showMinimised"];
let dockBusy = false;

async function renderDock() {
  if (dockBusy) return;
  const d = await Host.get().getDock();
  if (!d) return;
  for (const k of DOCK_SWITCHES) setSwitch(document.querySelector(`[data-dock="${k}"]`), d[k]);
  const when = document.querySelector('select[data-dock="when"]');
  if (document.activeElement !== when) when.value = d.when;
  $("dockOpts").classList.toggle("faded", !d.enabled);
  // With the dock on, Windows' taskbar is hidden: the options that only act on the real taskbar do nothing. Position, alignment and size still place the dock.
  for (const k of ["auto", "badges", "flash", "share", "corner", "combine", "search", "taskview"]) {
    document.querySelector(`[data-tb="${k}"]`)?.closest(".toggle-row")?.classList.toggle("faded", d.enabled);
  }
}

async function changeDock(name, value) {
  dockBusy = true;
  try {
    await Host.get().setDock(name, value);
  } finally {
    dockBusy = false;
  }
  await renderDock();
}

function startDock() {
  for (const k of DOCK_SWITCHES) {
    const el = document.querySelector(`[data-dock="${k}"]`);
    el.addEventListener("click", () => changeDock(k, el.classList.contains("on") ? 0 : 1));
  }
  document.querySelector('select[data-dock="when"]').addEventListener("change", e => changeDock("when", e.target.value));
}

function showTaskbarToast(text, undoState) {
  tbUndoState = undoState;
  $("tbToastText").textContent = text;
  $("tbUndo").hidden = !undoState;
  $("tbToast").hidden = false;
  clearTimeout(tbToastTimer);
  tbToastTimer = setTimeout(() => { $("tbToast").hidden = true; }, 9000);
}

/** Applies a whole saved state again (used by Undo): one option at a time, the same way the user would. */
async function applyTaskbarSnapshot(s) {
  const host = Host.get();
  const steps = [["position", s.position], ["align", s.align], ["size", s.small ? 1 : 0], ["badges", +s.badges], ["flash", +s.flash],
    ["share", +s.share], ["corner", +s.corner], ["combine", s.combine], ["search", +s.search], ["taskview", +s.taskview], ["auto", +s.auto]];
  let latest = null;
  for (const [name, value] of steps) latest = await host.setTaskbar(name, value);
  return latest;
}

async function changeTaskbar(name, value) {
  const host = Host.get();
  tbBusy = true;
  try {
    const before = await host.getTaskbar();
    const after = await host.setTaskbar(name, value);
    if (after) applyTaskbarState(after);
    $("tbRestore").disabled = false;
    showTaskbarToast(`${TB_NAMES[name]} updated`, before);
  } finally {
    tbBusy = false;
  }
}

function startTaskbar() {
  document.querySelectorAll("#tbPos .pos").forEach(p => p.addEventListener("click", () => changeTaskbar("position", Number(p.dataset.v))));
  for (const k of TB_SWITCHES) {
    const el = document.querySelector(`[data-tb="${k}"]`);
    el.addEventListener("click", () => changeTaskbar(k, el.classList.contains("on") ? 0 : 1));
  }
  document.querySelectorAll("select[data-tb]").forEach(el => el.addEventListener("change", () => changeTaskbar(el.dataset.tb, Number(el.value))));
  $("tbOpenWin").addEventListener("click", () => Host.get().openWindowsTaskbarSettings());
  $("tbRestore").addEventListener("click", async () => {
    tbBusy = true;
    try {
      const before = await Host.get().getTaskbar();
      const after = await Host.get().restoreTaskbar();
      if (after) applyTaskbarState(after);
      showTaskbarToast("Taskbar restored to how it was before Verso", before);
    } finally { tbBusy = false; }
  });
  $("tbUndo").addEventListener("click", async () => {
    const state = tbUndoState;
    $("tbToast").hidden = true;
    if (!state) return;
    tbBusy = true;
    try {
      const after = await applyTaskbarSnapshot(state);
      if (after) applyTaskbarState(after);
    } finally { tbBusy = false; }
  });
}

/** True while one of the two switches is waiting for the host, so the page does not redraw it back mid-click. */
let switching = false;

function startDesktop() {
  $("hideTaskbarSwitch").addEventListener("click", async () => {
    const next = !$("hideTaskbarSwitch").classList.contains("on");
    setSwitch($("hideTaskbarSwitch"), next);
    switching = true;
    try { await Host.get().setTaskbarHidden(next); } finally { switching = false; }
  });
  $("hideIconsSwitch").addEventListener("click", async () => {
    const next = !$("hideIconsSwitch").classList.contains("on");
    setSwitch($("hideIconsSwitch"), next);
    switching = true;
    try { await Host.get().setDesktopIconsHidden(next); } finally { switching = false; }
  });
}

/* ---- theme pickers ------------------------------------------------------ */

function renderThemeGrid(container, themes, selectedId, onPick) {
  container.innerHTML = "";

  for (const t of themes) {
    const card = document.createElement("button");
    card.type = "button";
    card.className = "theme-card" + (t.id === selectedId ? " selected" : "");
    card.setAttribute("aria-pressed", t.id === selectedId ? "true" : "false");
    const swatch = Array.isArray(t.swatch) && t.swatch.length
      ? `<div class="theme-card-preview swatches">${t.swatch.map(c => `<i style="background:${escapeHtml(c)}"></i>`).join("")}</div>`
      : `<div class="theme-card-preview card-preview" style="height:auto;">${escapeHtml(initials(t.name))}</div>`;
    card.innerHTML = `
      ${t.id === selectedId ? '<div class="theme-card-check">✓</div>' : ""}
      ${swatch}
      <div class="theme-card-label">${escapeHtml(t.name)}</div>`;
    card.addEventListener("click", () => onPick(t.id));
    container.appendChild(card);
  }
}

let deskThemeSig = "";

/** Outpost desk: its colour palettes (owned by the host). Classic desk: the old theme folders. */
async function renderDeskThemes(force = false) {
  const host = Host.get();
  const style = await host.getDeskStyle();

  if (style === "outpost") {
    $("deskThemeTitle").textContent = "Desk colours";
    const palettes = await host.getOutpostThemes();
    const chosen = palettes.find(p => p.selected)?.id ?? "";
    const sig = "o:" + JSON.stringify(palettes);
    if (!force && sig === deskThemeSig) return;
    deskThemeSig = sig;

    const container = $("deskThemeGrid");
    container.innerHTML = "";
    for (const p of palettes) {
      const card = document.createElement("button");
      card.type = "button";
      card.className = "theme-card" + (p.id === chosen ? " selected" : "");
      card.setAttribute("aria-pressed", p.id === chosen ? "true" : "false");
      card.innerHTML = `
        ${p.id === chosen ? '<div class="theme-card-check">✓</div>' : ""}
        <div class="theme-card-preview" style="background:linear-gradient(135deg,${escapeHtml(p.from)},${escapeHtml(p.to)});"></div>
        <div class="theme-card-label">${escapeHtml(p.name)}</div>`;
      card.addEventListener("click", async () => {
        await host.setOutpostTheme(p.id);
        renderDeskThemes(true);
      });
      container.appendChild(card);
    }
    return;
  }

  $("deskThemeTitle").textContent = "Desktop theme";
  const [themes, activeId] = await Promise.all([host.getThemes(), host.getThemeId()]);
  const sig = "c:" + activeId + JSON.stringify(themes);
  if (!force && sig === deskThemeSig) return;
  deskThemeSig = sig;
  renderThemeGrid($("deskThemeGrid"), themes, activeId, async id => {
    await host.setThemeId(id);
    renderDeskThemes(true);
  });
}

/* ---- settings tabs ------------------------------------------------------------ */

function startSettings() {
  document.querySelectorAll("#settingsTabs [data-tab]").forEach(tab => {
    tab.addEventListener("click", () => {
      document.querySelectorAll("#settingsTabs [data-tab]").forEach(t => t.classList.toggle("active", t === tab));
      document.querySelectorAll('.view[data-view="settings"] .tab-panel').forEach(p =>
        p.classList.toggle("active", p.dataset.panel === tab.dataset.tab));
    });
  });

  // The palette can also be changed from the desk's round menu; while the Theme tab is showing, follow it.
  setInterval(() => {
    const view = document.querySelector('[data-view="settings"]');
    const panel = document.querySelector('.tab-panel[data-panel="theme"]');
    if (view?.classList.contains("active") && panel?.classList.contains("active")) renderDeskThemes();

    // same for the Desktop tab: hide taskbar / icons can also be flipped from the desk menu or the tray
    const desktop = document.querySelector('.tab-panel[data-panel="desktop"]');
    if (view?.classList.contains("active") && desktop?.classList.contains("active") && !switching) renderDesktop();
  }, 1500);
}

function renderDashboardThemes() {
  const active = Theme.current();
  renderThemeGrid($("dashboardThemeGrid"), Theme.list(), active?.id, async id => {
    await Theme.apply(id);
    renderDashboardThemes();
  });
}

/* ---- start -------------------------------------------------------------- */

async function start() {
  await Theme.start();
  Theme.onChange(() => renderDashboardThemes());

  await Host.connect();

  Titlebar.start();
  startNav();
  startWallpaper();
  startWidgets();
  startApps();
  startDesktop();
  startTaskbar();
  startDock();
  startSettings();

  $("sidebarFoot").textContent = Host.get().kind === "demo" ? "Verso (demo)" : "Verso";

  await Promise.all([
    renderWallpaper(),
    renderWidgets(),
    renderApps(true),
    renderDesktop(),
    renderDeskThemes()
  ]);
  renderDashboardThemes();
}

start();
