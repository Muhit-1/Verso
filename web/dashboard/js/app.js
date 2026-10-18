/* ==================================================================
   VERSO DASHBOARD — bootstrap

   Theme first and awaited, then the host, then every section is
   rendered from real data — no fake widget previews or theme swatches
   here; a card shows what it actually knows (name, description,
   on/off state) rather than a rendering it cannot back up.
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
  if (!bg || bg.kind === "none") return "None set — the theme's own backdrop shows.";
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
      <div class="card-preview">${escapeHtml(initials(w.name))}</div>
      <div class="card-foot">
        <div>
          <div class="card-name">${escapeHtml(w.name)}</div>
          <div class="card-sub">${escapeHtml(w.description ?? "")}</div>
        </div>
        <button type="button" class="switch${w.enabled ? " on" : ""}" data-widget-id="${escapeHtml(w.id)}" aria-label="Toggle ${escapeHtml(w.name)}">
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

    section.querySelector("[data-add]").addEventListener("click", async () => {
      await Host.get().addApp(box.id);
      renderApps(true);
    });
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
  $("addAppBtn").addEventListener("click", async () => {
    await Host.get().addApp("recent");
    renderApps(true);
  });
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
}

function startDesktop() {
  $("hideTaskbarSwitch").addEventListener("click", async () => {
    const next = !$("hideTaskbarSwitch").classList.contains("on");
    setSwitch($("hideTaskbarSwitch"), next);
    await Host.get().setTaskbarHidden(next);
  });
  $("hideIconsSwitch").addEventListener("click", async () => {
    const next = !$("hideIconsSwitch").classList.contains("on");
    setSwitch($("hideIconsSwitch"), next);
    await Host.get().setDesktopIconsHidden(next);
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
    card.innerHTML = `
      ${t.id === selectedId ? '<div class="theme-card-check">✓</div>' : ""}
      <div class="theme-card-preview card-preview" style="height:auto;">${escapeHtml(initials(t.name))}</div>
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
