/* ==================================================================
   VERSO — Outpost core: saved state, palettes, usable area.
================================================================== */

import * as Host from "/shell/js/host.js";

const $ = s => document.querySelector(s);

/* ---- saved state (opaque to the host; see ScriptBridge.GetDeskState) ---- */

let state = {};
let timer = null;

export function loadState() {
  const s = Host.get().getDeskState?.();
  state = s && typeof s === "object" ? s : {};
  return state;
}

export const getState = () => state;

export function patchState(patch) {
  Object.assign(state, patch);
  scheduleSave();
}

export function setPosition(id, value) {
  state.pos = { ...(state.pos || {}), [id]: value };
  scheduleSave();
}

/* Each widget saves only under its own key (state.w.<id>), so one widget's data can never overwrite another's. */
export function getWidgetState(id) {
  return (state.w && state.w[id]) || {};
}

export function patchWidgetState(id, patch) {
  state.w = { ...(state.w || {}), [id]: { ...getWidgetState(id), ...patch } };
  scheduleSave();
}

/** Forgets one element's saved position (a deleted note or box). */
export function clearPosition(key) {
  if (state.pos && key in state.pos) {
    const next = { ...state.pos };
    delete next[key];
    state.pos = next;
    scheduleSave();
  }
}

export function clearPositions() {
  state.pos = {};
  scheduleSave();
}

/** Writes the state now instead of after the usual short delay (used before a reload). */
export function flush() {
  clearTimeout(timer);
  Host.get().saveDeskState?.(state);
}

function scheduleSave() {
  clearTimeout(timer);
  timer = setTimeout(() => Host.get().saveDeskState?.(state), 250);
}

/* ---- palettes (a "theme" is colour only: the wallpaper is the user's) ----
   The chosen palette is kept by the host (settings.outpostTheme), so the dashboard and this desk always agree. */

export const PALETTES = ["sunset", "night", "evening", "forest-green", "black-sunset", "pencil"];

export function applyPalette(name, announce = false) {
  const id = PALETTES.includes(name) ? name : PALETTES[0];
  document.body.dataset.theme = id;
  if (announce) {
    const t = $("#toast");
    t.textContent = id.replace("-", " ");
    t.classList.add("show");
    clearTimeout(applyPalette.h);
    applyPalette.h = setTimeout(() => t.classList.remove("show"), 1400);
  }
  return id;
}

/* ---- the area elements may use ----
   The host passes ?wa=left,top,width,height in PHYSICAL pixels, relative to this monitor, for the
   part not covered by the taskbar. In a plain browser the whole viewport is used. */

const wa = new URLSearchParams(location.search).get("wa");

export function area() {
  if (wa) {
    const dpr = window.devicePixelRatio || 1;
    const [l, t, w, h] = wa.split(",").map(Number);
    if ([l, t, w, h].every(Number.isFinite) && w > 0 && h > 0) {
      return { x: l / dpr, y: t / dpr, w: w / dpr, h: h / dpr };
    }
  }
  return { x: 0, y: 0, w: window.innerWidth, h: window.innerHeight };
}

/* ---- where an element is on the SCREEN, in physical pixels ----
   The pop-up editor is a real window placed exactly over an element, so the host needs screen pixels. The host
   passes this monitor's origin as ?mo=left,top (physical pixels); the page's own scale is devicePixelRatio. */

const mo = (new URLSearchParams(location.search).get("mo") || "0,0").split(",").map(Number);

export function screenRect(el) {
  const r = el.getBoundingClientRect();
  const dpr = window.devicePixelRatio || 1;
  return {
    x: Math.round((mo[0] || 0) + r.left * dpr),
    y: Math.round((mo[1] || 0) + r.top * dpr),
    w: Math.round(r.width * dpr),
    h: Math.round(r.height * dpr)
  };
}
