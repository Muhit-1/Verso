/* ==================================================================
   VERSO — Outpost layout

   Every .mv element is absolutely positioned (left/top in px) and movable:

   - ARRANGE MODE (the MOVE button): every element is picked up by its body. Snaps to 32 px margins
     and to the centre lines (with guides), and is clamped to the area above the taskbar.
   - PANELS move by their title, any time.
   - AUTO ORIENTATION: the dock and the two button groups turn vertical on their own while their
     centre is within 150 px of the left/right edge of the usable area, and horizontal again away from it.

   Positions are stored as FRACTIONS OF THE VIEWPORT, so a desk arranged on a 4K screen still makes
   sense on a 1080p one, and are clamped on load so nothing can be restored off-screen.
================================================================== */

import * as Core from "./core.js";

const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];

const MARGIN = 32;
const EDGE = 150;
const SNAP = 14;

let A = Core.area();
const mvs = $$(".mv");
const byId = id => $(`.mv[data-id="${id}"]`);
const size = el => ({ w: el.offsetWidth, h: el.offsetHeight });
const clampX = (x, w) => Math.min(Math.max(A.x, x), A.x + A.w - w);
const clampY = (y, h) => Math.min(Math.max(A.y, y), A.y + A.h - h);

function put(el, x, y) {
  const { w, h } = size(el);
  el.style.left = clampX(x, w) + "px";
  el.style.top = clampY(y, h) + "px";
}

/** Vertical while the element's centre is near the left/right edge. Returns whether it flipped. */
function orient(el, x) {
  if (el.dataset.orient !== "auto") return false;
  const { w } = size(el);
  const cx = x + w / 2;
  const vertical = cx < A.x + EDGE || cx > A.x + A.w - EDGE;
  const was = el.classList.contains("v");
  el.classList.toggle("v", vertical);
  return vertical !== was;
}

/* ---- default composition ---- */

function defaults() {
  const set = (id, fn) => {
    const el = byId(id);
    if (el.classList.contains("panel")) return;
    el.classList.remove("v");
    const { w, h } = size(el);
    const [x, y] = fn(w, h);
    put(el, x, y);
  };
  // 112 px from the left keeps the pill clear of the desktop icon column.
  set("np", () => [A.x + 112, A.y + 32]);
  set("tr", w => [A.x + A.w - MARGIN - w, A.y + 32]);
  set("clock", (w, h) => [A.x + 48, A.y + A.h - 36 - h]);
  set("dock", (w, h) => [A.x + (A.w - w) / 2, A.y + A.h - 20 - h]);
  set("br", (w, h) => [A.x + A.w - MARGIN - w, A.y + A.h - 20 - h]);
}

function applySaved() {
  const pos = Core.getState().pos || {};
  for (const el of mvs) {
    const s = pos[el.dataset.id];
    if (!s || el.classList.contains("panel")) continue;
    if (el.dataset.orient === "auto") el.classList.toggle("v", !!s.v);
    put(el, s.fx * innerWidth, s.fy * innerHeight);
  }
}

export function layout() {
  A = Core.area();
  defaults();
  applySaved();
  const hint = $("#hint");
  hint.style.top = A.y + 26 + "px";
}

/* ---- panels ---- */

export function placePanel(p) {
  const s = (Core.getState().pos || {})[p.dataset.id];
  if (s) {
    put(p, s.fx * innerWidth, s.fy * innerHeight);
    return;
  }
  const d = byId("dock");
  const { w: dw, h: dh } = size(d);
  const dx = d.offsetLeft;
  const dy = d.offsetTop;
  const { w: pw, h: ph } = size(p);
  let x;
  let y;
  if (d.classList.contains("v")) {
    y = dy + dh / 2 - ph / 2;
    x = dx + dw / 2 < innerWidth / 2 ? dx + dw + 18 : dx - pw - 18;
  } else {
    x = dx + dw / 2 - pw / 2;
    y = dy + dh / 2 > A.y + A.h / 2 ? dy - ph - 16 : dy + dh + 16;
  }
  put(p, x, y);
}

export function closePanels() {
  $$(".panel").forEach(p => p.classList.remove("open"));
  $$("#dock button").forEach(b => b.classList.remove("active"));
}

export function openPanel(id) {
  const p = $("#" + id);
  const was = p.classList.contains("open");
  closePanels();
  if (!was) {
    placePanel(p);
    p.classList.add("open");
    $(`#dock [data-p="${id}"]`).classList.add("active");
  }
}

/* ---- dragging ---- */

let drag = null;
const gx = $("#gx");
const gy = $("#gy");

addEventListener("pointerdown", e => {
  const el = e.target.closest(".mv");
  if (!el) return;
  const isPanel = el.classList.contains("panel");
  if (isPanel ? !e.target.closest("h3") : !document.body.classList.contains("arrange")) return;
  const { w, h } = size(el);
  drag = {
    el,
    fx: (e.clientX - el.offsetLeft) / w, // grab point as a fraction: survives a size change on flip
    fy: (e.clientY - el.offsetTop) / h
  };
  el.classList.add("dragging");
  try { el.setPointerCapture(e.pointerId); } catch { /* not an active pointer (mirrored mouse) — window listeners still get it */ }
  e.preventDefault();
});

addEventListener("pointermove", e => {
  if (!drag) return;
  const { el } = drag;
  let { w, h } = size(el);
  let x = e.clientX - drag.fx * w;
  let y = e.clientY - drag.fy * h;
  if (orient(el, x)) {
    ({ w, h } = size(el));
    x = e.clientX - drag.fx * w;
    y = e.clientY - drag.fy * h;
  }

  let sx = null;
  let sy = null;
  for (const c of [A.x + MARGIN, A.x + (A.w - w) / 2, A.x + A.w - MARGIN - w]) if (Math.abs(x - c) < SNAP) { x = c; sx = c; }
  for (const c of [A.y + MARGIN, A.y + (A.h - h) / 2, A.y + A.h - MARGIN - h]) if (Math.abs(y - c) < SNAP) { y = c; sy = c; }
  const midX = A.x + (A.w - w) / 2;
  const midY = A.y + (A.h - h) / 2;
  gx.style.left = A.x + A.w / 2 + "px";
  gy.style.top = A.y + A.h / 2 + "px";
  gx.classList.toggle("on", sx !== null && Math.abs(sx - midX) < 1);
  gy.classList.toggle("on", sy !== null && Math.abs(sy - midY) < 1);
  put(el, x, y);
});

addEventListener("pointerup", () => {
  if (!drag) return;
  const { el } = drag;
  el.classList.remove("dragging");
  gx.classList.remove("on");
  gy.classList.remove("on");
  Core.setPosition(el.dataset.id, {
    fx: el.offsetLeft / innerWidth,
    fy: el.offsetTop / innerHeight,
    v: el.classList.contains("v")
  });
  // an unplaced open panel follows the dock
  if (el.id === "dock") {
    const pos = Core.getState().pos || {};
    $$(".panel.open").forEach(p => { if (!pos[p.dataset.id]) placePanel(p); });
  }
  drag = null;
});

/* ---- arrange mode ---- */

export function arrange(on) {
  document.body.classList.toggle("arrange", on);
  $("#bMove").classList.toggle("on", on);
  if (on) closePanels();
}

export const isArranging = () => document.body.classList.contains("arrange");

export function resetLayout() {
  Core.clearPositions();
  $$(".panel").forEach(p => { p.style.left = p.style.top = ""; });
  layout();
}

export function start() {
  $("#bMove").addEventListener("click", () => arrange(!isArranging()));
  $("#hDone").addEventListener("click", () => arrange(false));
  $("#hReset").addEventListener("click", resetLayout);
  addEventListener("resize", layout);
  document.fonts.ready.then(layout);
  layout();
}
