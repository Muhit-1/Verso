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

function describeBackground(bg) {
  if (!bg || bg.kind === "none") return "None set — the theme's own backdrop shows.";
  if (bg.kind === "image") return "Image — applied to the desktop wallpaper directly.";
  const name = bg.url ? decodeURIComponent(bg.url.split("/").pop()) : "";
  return `${bg.kind === "video" ? "Video" : "Interactive"}: ${name}`;
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
    grid.appendChild(card);
  }

  grid.querySelectorAll("[data-widget-id]").forEach(btn => {
    btn.addEventListener("click", async () => {
      const enabled = !btn.classList.contains("on");
      setSwitch(btn, enabled);
      await Host.get().setWidgetEnabled(btn.dataset.widgetId, enabled);
    });
  });
}

function startWidgets() {
  $("resetLayoutBtn").addEventListener("click", () => Host.get().resetLayout());
}

/* ---- apps (launcher) --------------------------------------------------- */

async function renderLauncher() {
  const entries = await Host.get().getLauncherEntries();
  const list = $("launcherList");

  if (!entries.length) {
    list.innerHTML = '<div class="list-empty">No apps added yet.</div>';
    return;
  }

  list.innerHTML = entries.map(e => `
    <div class="list-row">
      <span class="name">${escapeHtml(e.name)}</span>
      <button type="button" class="icon-btn" data-remove-id="${escapeHtml(e.id)}" aria-label="Remove ${escapeHtml(e.name)}">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 6l12 12M18 6L6 18"/></svg>
      </button>
    </div>`).join("");

  list.querySelectorAll("[data-remove-id]").forEach(btn => {
    btn.addEventListener("click", async () => {
      await Host.get().removeLauncherEntry(btn.dataset.removeId);
      renderLauncher();
    });
  });
}

function startLauncher() {
  $("addLauncherBtn").addEventListener("click", async () => {
    await Host.get().addLauncherEntry();
    renderLauncher();
  });
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

async function renderDeskThemes() {
  const [themes, activeId] = await Promise.all([Host.get().getThemes(), Host.get().getThemeId()]);
  renderThemeGrid($("deskThemeGrid"), themes, activeId, async id => {
    await Host.get().setThemeId(id);
    renderDeskThemes();
  });
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
  startLauncher();
  startDesktop();

  $("sidebarFoot").textContent = Host.get().kind === "demo" ? "Verso (demo)" : "Verso";

  await Promise.all([
    renderWallpaper(),
    renderWidgets(),
    renderLauncher(),
    renderDesktop(),
    renderDeskThemes()
  ]);
  renderDashboardThemes();
}

start();
