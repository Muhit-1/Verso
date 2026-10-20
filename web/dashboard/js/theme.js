/* ==================================================================
   VERSO DASHBOARD — theme loader

   Same discover/apply shape as web/shell/js/theme.js, pointed at a
   different contract: see docs/dashboard-theme-spec.md. Trimmed
   relative to the shell's version — no ?theme= URL override, no
   "press T" toast, because the dashboard has a real theme picker UI
   instead of being the only theme UI that exists.
================================================================== */

export const THEME_ROOT = "themes";
const USER_THEME_ROOT = "https://verso.user-dashboard-themes.example";

const STORE_KEY = "verso.dashboard-theme.v1";
const FALLBACK_ID = "fieldnote";

let manifests = [];
let currentId = null;
const listeners = [];

/* ---- discovery -------------------------------------------------- */

function bridge() {
  return window.chrome?.webview?.hostObjects?.VersoDashboard ?? null;
}

async function loadManifest(id) {
  try {
    const res = await fetch(`${THEME_ROOT}/${id}/theme.json`, { cache: "no-cache" });
    if (!res.ok) return null;
    const m = await res.json();
    return { ...m, id };
  } catch {
    return null;
  }
}

async function discover() {
  const host = bridge();

  if (host) {
    try {
      const json = await host.GetDashboardThemes();
      const parsed = JSON.parse(json);
      if (Array.isArray(parsed) && parsed.length) return parsed;
    } catch {
      /* fall through to the index file */
    }
  }

  try {
    const res = await fetch(`${THEME_ROOT}/themes.json`, { cache: "no-cache" });
    const index = await res.json();
    const found = await Promise.all((index.themes ?? []).map(loadManifest));
    return found.filter(Boolean);
  } catch (err) {
    console.warn(`Verso dashboard: could not read ${THEME_ROOT}/themes.json — only`
      + " the active theme will be available.", err);
    return [];
  }
}

/* ---- applying --------------------------------------------------- */

function ensureLink(id, attrs) {
  let el = document.getElementById(id);
  if (!el) {
    el = document.createElement("link");
    el.id = id;
    el.rel = "stylesheet";
    document.head.appendChild(el);
  }
  Object.assign(el, attrs);
  return el;
}

/** Swaps the active theme in place. No window reload, ever. */
export async function apply(id) {
  const manifest = manifests.find(t => t.id === id) ?? await loadManifest(id);
  if (!manifest) return null;

  // A font stylesheet is a full https URL, or a path relative to the theme's own folder ("../../fonts/fonts.css" = the bundled, offline set).
  const themeBase = new URL(`${manifest.source === "user" ? USER_THEME_ROOT : THEME_ROOT}/${id}/`, document.baseURI);
  const fonts = (manifest.fonts ?? []).map(href => new URL(href, themeBase).href);
  fonts.forEach((href, i) => ensureLink(`verso-font-${i}`, { href }));
  for (let i = fonts.length; ; i++) {
    const stale = document.getElementById(`verso-font-${i}`);
    if (!stale) break;
    stale.remove();
  }

  const root = manifest.source === "user" ? USER_THEME_ROOT : THEME_ROOT;
  const sheet = manifest.stylesheet ?? "theme.css";
  const link = ensureLink("verso-theme", { href: `${root}/${id}/${sheet}` });

  await new Promise(resolve => {
    if (link.sheet) return resolve();
    link.addEventListener("load", resolve, { once: true });
    link.addEventListener("error", resolve, { once: true });
  });

  currentId = id;
  document.documentElement.dataset.theme = id;
  document.documentElement.dataset.scheme = manifest.scheme ?? "light";

  try { localStorage.setItem(STORE_KEY, id); } catch { /* storage disabled */ }
  try { bridge()?.SetDashboardThemeId(id); } catch { /* no host */ }

  listeners.forEach(cb => cb(manifest));
  return manifest;
}

export function list() { return manifests.slice(); }
export function current() { return manifests.find(t => t.id === currentId) ?? null; }
export function onChange(cb) { listeners.push(cb); }

/* ---- start ------------------------------------------------------ */

/** Order: what the host has saved, then what this browser last used, then the index's default. */
export async function start() {
  manifests = await discover();

  let fromHost = null;
  try { fromHost = await bridge()?.GetDashboardThemeId(); } catch { /* no host */ }

  let fromStore = null;
  try { fromStore = localStorage.getItem(STORE_KEY); } catch { /* storage disabled */ }

  const known = id => id && manifests.some(t => t.id === id);
  const id = [fromHost, fromStore, FALLBACK_ID].find(known)
    ?? manifests[0]?.id
    ?? FALLBACK_ID;

  return apply(id);
}
