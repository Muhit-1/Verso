/* ==================================================================
   VERSO — widgets

   Renders installed widget folders into the desk: web/widgets/* and
   the user's own widgets folder (wherever the data root has been
   redirected to — see VersoPaths). Phase 5.

   Each widget gets:
     - its own iframe, so its CSS can never touch another's and a
       crash inside it cannot take the desk down (docs/widget-spec.md)
     - the current theme's stylesheet injected into that iframe, so
       var(--chalk) etc. resolve the same way they do in the shell —
       an iframe is a separate document, and CSS custom properties do
       not cross that boundary on their own
     - window.VersoHost set to a *permission-scoped* view of the
       already-connected host: only the methods its widget.json
       declared, per docs/widget-spec.md

   Two things this file exists to get right, in order:

   1. INJECTION TIMING. The parent can only reach into an iframe's
      document after that iframe has loaded — which is after the
      widget's own top-level script has already run. So window.VersoHost
      is never present on the very first line of a widget's script. See
      the timing note on rule 1 in docs/widget-spec.md: widgets look it
      up fresh inside paint(), not once at the top.

   2. DRAGGING. Pointer events do not bubble out of a cross-document
      iframe into the parent's listeners — a platform security boundary,
      not a bug. js/layout.js's "drag from anywhere in the panel" would
      never fire for a click landing on a widget's own content. Every
      loaded widget gets a small drag handle instead: a thin strip that
      is part of the wrapper (the parent document, not the iframe),
      which is plain same-document DOM and so bubbles normally to the
      pointerdown listener layout.js already attaches to [data-panel].
================================================================== */

import * as Host from "./host.js";
import * as Theme from "./theme.js";
import * as Layout from "./layout.js";

// Relative, like theme.js's THEME_ROOT — resolves against whatever origin
// the shell is actually running under, native virtual host or the plain
// dev server both. An absolute "https://verso.local/..." here would work
// in the native app and silently 404 in a plain browser.
const BUNDLED_BASE = "../widgets";

// User widgets have no plain-browser equivalent — there is no folder to
// discover them from without a host — so this is native-only, and never
// exercised when it is not (see discoverDemoWidgets in host.js, which
// only ever returns source:"bundled").
const USER_BASE = "https://verso.user-widgets.local";

/**
 * Where a panel renders until the user drags or resizes it — see
 * js/layout.js's registerDefault. Fractions of the viewport, chosen to
 * reproduce the desk's original composition: the dial roughly where the
 * old grid's centre column put it, tasks/player/anything else stacked
 * down the old rail on the right, the left edge kept clear for desktop
 * icons the way `--safe` used to as a grid column.
 *
 * Anything not listed here — a widget dropped in later, bundled or
 * user-installed — cascades down the same right-hand column below the
 * last known one, which is why the fallback below is a formula and not
 * another hardcoded entry: adding a widget must never mean editing this
 * file, that is the entire point of Phase 5.
 */
const DEFAULT_POSITIONS = {
  dial:   { x: 0.14, y: 0.30, w: 0.46 },
  tasks:  { x: 0.70, y: 0.06, w: 0.27 },
  player: { x: 0.70, y: 0.46, w: 0.27 }
};

// An explicit h, unlike the three named defaults above — a fallback
// widget's *intrinsic* content height is unknown here (it hasn't loaded
// yet), so without one, stacking two or more the same way tasks/player
// stack could ask for more room than is actually left below them and
// clamp every extra widget to the same bottom-of-screen position,
// overlapping them exactly. A modest fixed height sidesteps that.
const FALLBACK_COLUMN = { x: 0.70, w: 0.27, h: 0.12 };
const FALLBACK_START_Y = 0.68;
const FALLBACK_STEP_Y = 0.13;
let fallbackSlot = 0;

/** The "dial" wrapper gets the shell's special naked-in-wallpaper-mode chrome; everything else gets the standard panel chrome. */
function chromeClassFor(id) {
  return id === "dial" ? "dial" : "comp";
}

function defaultPositionFor(id) {
  if (DEFAULT_POSITIONS[id]) {
    return DEFAULT_POSITIONS[id];
  }

  const pos = { ...FALLBACK_COLUMN, y: FALLBACK_START_Y + fallbackSlot * FALLBACK_STEP_Y };
  fallbackSlot++;
  return pos;
}

/**
 * Which host.js methods each permission unlocks. Not part of the widget
 * itself — the loader builds this view fresh per widget, from the one
 * host connection the shell already made.
 */
const PERMISSION_METHODS = {
  tasks: ["loadTasks", "saveTasks"],
  notes: ["loadNotes", "saveNotes"],
  agenda: ["loadAgenda", "saveAgenda"],
  media: ["getNowPlaying", "getQueue", "play", "pause", "next", "previous", "onNowPlayingChanged"],
  apps: ["getLauncherEntries", "launchApp"]
};

/**
 * A view of the already-connected host exposing only what this widget
 * declared. Ask for nothing and the object carries nothing beyond
 * identity — see docs/widget-spec.md.
 */
function scopeHost(full, permissions) {
  const granted = new Set(permissions ?? []);
  const scoped = { kind: full.kind, hostVersion: full.hostVersion };

  for (const [permission, methods] of Object.entries(PERMISSION_METHODS)) {
    if (!granted.has(permission)) continue;
    for (const name of methods) {
      if (typeof full[name] === "function") {
        scoped[name] = full[name].bind(full);
      }
    }
  }

  return scoped;
}

function widgetUrl(manifest) {
  const entry = manifest.entry || "index.html";
  const base = manifest.source === "user" ? USER_BASE : BUNDLED_BASE;

  // The shell knows its own layer mode (see applyLayer() in desk.js) —
  // a widget does not, and cannot be told after the fact the way the
  // theme link is injected, because a glow that needs suppressing in
  // overlay mode (a box-shadow clipped hard at the region edge looks
  // wrong) has to not be painted in the first place, not painted and
  // then patched from outside an iframe boundary CSS cannot cross.
  const layer = document.documentElement.dataset.layer || "all";
  return `${base}/${manifest.id}/${entry}?layer=${layer}`;
}

/**
 * Injects the shell's own currently-active theme links into a widget's
 * document. A no-op for a cross-origin (user) widget — contentDocument
 * is inaccessible there by the same-origin policy, same as it would be
 * opening a cross-site page in a normal browser tab. That widget still
 * renders; it just renders the way it does when opened standalone,
 * which every widget is already required to support.
 */
function applyThemeTo(frameDoc) {
  if (!frameDoc || !frameDoc.head) return;

  const shellTheme = document.getElementById("verso-theme");
  if (shellTheme) {
    let link = frameDoc.getElementById("verso-theme");
    if (!link) {
      link = frameDoc.createElement("link");
      link.id = "verso-theme";
      link.rel = "stylesheet";
      frameDoc.head.appendChild(link);
    }
    link.href = shellTheme.href;
  }

  // Fonts too — a widget using --font-display before its face has
  // loaded is a widget rendering in a fallback serif for a second.
  for (const shellFont of document.querySelectorAll('link[id^="verso-font-"]')) {
    let link = frameDoc.getElementById(shellFont.id);
    if (!link) {
      link = frameDoc.createElement("link");
      link.id = shellFont.id;
      link.rel = "stylesheet";
      frameDoc.head.appendChild(link);
    }
    link.href = shellFont.href;
  }
}

const loadedFrames = [];

function mount(manifest, host) {
  const panelId = manifest.id; // stable across sessions — see Layout.registerDefault

  const wrapper = document.createElement("div");
  wrapper.className = `${chromeClassFor(manifest.id)} widget-frame`;
  wrapper.dataset.panel = panelId;
  wrapper.setAttribute("aria-label", manifest.name || manifest.id);

  const handle = document.createElement("div");
  handle.className = "handle";
  handle.title = manifest.name || manifest.id;
  wrapper.appendChild(handle);

  const iframe = document.createElement("iframe");
  iframe.title = manifest.name || manifest.id;
  iframe.addEventListener("load", () => {
    let frameDoc = null;
    try {
      frameDoc = iframe.contentDocument;
    } catch {
      // Cross-origin. Nothing more to do for this widget.
    }
    applyThemeTo(frameDoc);

    try {
      iframe.contentWindow.VersoHost = scopeHost(host, manifest.permissions);
    } catch {
      // Same cross-origin case. The widget still rendered its own
      // markup, which is a far better failure than none at all.
    }
  });
  iframe.src = widgetUrl(manifest);

  wrapper.appendChild(iframe);
  document.body.appendChild(wrapper);
  loadedFrames.push(iframe);

  Layout.registerDefault(panelId, defaultPositionFor(panelId));
}

export function start() {
  const host = Host.get();
  const manifests = (host.getWidgets?.() ?? []).filter(m => m.enabled !== false);

  for (const manifest of manifests) {
    mount(manifest, host);
  }

  // A theme switch (press T) has to reach widgets already on screen,
  // not just the shell — otherwise the desk and its own widgets
  // visibly disagree about which theme is active.
  Theme.onChange(() => {
    for (const iframe of loadedFrames) {
      let frameDoc = null;
      try {
        frameDoc = iframe.contentDocument;
      } catch {
        continue;
      }
      applyThemeTo(frameDoc);
    }
  });
}
