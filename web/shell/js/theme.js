/* ==================================================================
   VERSO — theme loader

   Finds the installed themes, injects the chosen one's fonts and
   stylesheet, and remembers the choice.

   Two sources, in order of preference:
     1. the native host, which scans web/themes and %APPDATA%\Verso\themes
     2. web/themes/themes.json, for when the page is opened in a plain
        browser and cannot list a directory

   Loading a theme is a link swap. It is deliberately not a rebuild, a
   class on <html>, or a set of CSS custom properties written from JS:
   a stylesheet swap is the only version where a theme someone else
   wrote can be dropped in without the shell knowing anything about it.
================================================================== */

export const THEME_ROOT = "../themes";

/**
 * Where a *user-installed* theme's files actually are. Distinct from
 * THEME_ROOT because a user theme lives in %APPDATA%\Verso\themes (or
 * wherever that has been redirected to — see docs/plan.md's data-root
 * amendment), a different real folder from the bundled web layer, mapped
 * to its own virtual host in DeskWindow.cs. Building every theme's URL
 * from THEME_ROOT unconditionally was a bug: a user theme's own CSS
 * would 404 the instant the page tried to load it, even though the host
 * found the theme fine.
 */
const USER_THEME_ROOT = "https://verso.user-themes.example";

const STORE_KEY = "verso.theme.v1";
const FALLBACK_ID = "smoked-petrol";

let manifests = [];
let currentId = null;
const listeners = [];

/* ---- discovery -------------------------------------------------- */

function bridge() {
  return window.chrome?.webview?.hostObjects?.VersoHost ?? null;
}

async function loadManifest(id) {
  try {
    const res = await fetch(`${THEME_ROOT}/${id}/theme.json`, { cache: "no-cache" });
    if (!res.ok) return null;
    const m = await res.json();
    // The folder name wins. An id that disagrees with its folder would
    // break every relative URL built from it.
    return { ...m, id };
  } catch {
    return null;
  }
}

async function discover() {
  const host = bridge();

  if (host) {
    try {
      const json = await host.GetThemes();
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
    // Worth saying out loud. Without it, a typo in themes.json shows up
    // only as "pressing T does nothing", which is a wasted evening.
    console.warn(`Verso: could not read ${THEME_ROOT}/themes.json — only the`
      + " active theme will be available.", err);
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

/**
 * Swaps the active theme. Resolves once its stylesheet has actually
 * loaded, so nothing paints against a half-applied palette.
 */
export async function apply(id) {
  const manifest = manifests.find(t => t.id === id) ?? await loadManifest(id);
  if (!manifest) return null;

  // Fonts first: they are a separate origin and slower, and a face that
  // arrives after the stylesheet causes a visible reflow of the clock.
  const fonts = manifest.fonts ?? [];
  fonts.forEach((href, i) => ensureLink(`verso-font-${i}`, { href }));
  // Drop links belonging to a previous theme that declared more faces.
  for (let i = fonts.length; ; i++) {
    const stale = document.getElementById(`verso-font-${i}`);
    if (!stale) break;
    stale.remove();
  }

  // A user theme's files live under a different virtual host than a
  // bundled one — see USER_THEME_ROOT above.
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
  document.documentElement.dataset.scheme = manifest.scheme ?? "dark";

  try { localStorage.setItem(STORE_KEY, id); } catch { /* storage disabled */ }
  try { bridge()?.SetThemeId(id); } catch { /* no host */ }

  listeners.forEach(cb => cb(manifest));
  return manifest;
}

/**
 * Moves to the next installed theme. Bound to T on the desk, which is
 * the only theme UI that exists until the settings window lands.
 */
export function cycle(step = 1) {
  if (manifests.length < 2) return null;
  const at = manifests.findIndex(t => t.id === currentId);
  const next = manifests[(at + step + manifests.length) % manifests.length];
  return apply(next.id);
}

export function list() { return manifests.slice(); }
export function current() { return manifests.find(t => t.id === currentId) ?? null; }
export function onChange(cb) { listeners.push(cb); }

/* ---- start ------------------------------------------------------ */

/**
 * Resolves the theme to use and applies it.
 *
 * Order: an explicit ?theme= in the URL, then what the host has saved,
 * then what this browser last used, then the index's default. The URL
 * wins so a theme can be checked without disturbing the saved choice —
 * except it does get saved, which is the honest trade for one fewer
 * code path.
 */
export async function start() {
  manifests = await discover();

  const fromUrl = new URLSearchParams(location.search).get("theme");

  let fromHost = null;
  try { fromHost = await bridge()?.GetThemeId(); } catch { /* no host */ }

  let fromStore = null;
  try { fromStore = localStorage.getItem(STORE_KEY); } catch { /* storage disabled */ }

  const known = id => id && manifests.some(t => t.id === id);
  const id = [fromUrl, fromHost, fromStore, FALLBACK_ID].find(known)
    ?? manifests[0]?.id
    ?? FALLBACK_ID;

  return apply(id);
}
