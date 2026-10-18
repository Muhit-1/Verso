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

export function clearPositions() {
  state.pos = {};
  scheduleSave();
}

function scheduleSave() {
  clearTimeout(timer);
  timer = setTimeout(() => Host.get().saveDeskState?.(state), 250);
}

/* ---- palettes (a "theme" is colour only: the wallpaper is the user's) ---- */

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

export function cyclePalette() {
  const current = PALETTES.indexOf(document.body.dataset.theme);
  const next = PALETTES[(current + 1) % PALETTES.length];
  applyPalette(next, true);
  patchState({ theme: next });
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
