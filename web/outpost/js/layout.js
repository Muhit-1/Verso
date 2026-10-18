/* ==================================================================
   VERSO — Outpost layout engine

   Positions and moves elements. It knows nothing about any widget: it only sees elements with a key.

   - Every desk element is absolutely positioned (left/top in px) and saved under its own key as FRACTIONS OF
     THE VIEWPORT, so a desk arranged on a 4K screen still makes sense on a 1080p one. Clamped on load.
   - ARRANGE MODE: every element is picked up by its body; snaps to 32 px margins and the centre lines (with
     guides); resizable elements (data-rs) get a corner grip.
   - Outside arrange mode an element moves by its [data-drag] handle (a card's title, a note's strip...).
   - Moving or resizing one element writes only that element's position.
================================================================== */

import * as Core from "./core.js";

const $ = s => document.querySelector(s);

const MARGIN = 32;
const SNAP = 14;

let A = Core.area();
let desk;
let gx;
let gy;
let drag = null;

const size = el => ({ w: el.offsetWidth, h: el.offsetHeight });
const clampX = (x, w) => Math.min(Math.max(A.x, x), A.x + A.w - w);
const clampY = (y, h) => Math.min(Math.max(A.y, y), A.y + A.h - h);

function put(el, x, y) {
  const { w, h } = size(el);
  el.style.left = clampX(x, w) + "px";
  el.style.top = clampY(y, h) + "px";
}

export const area = () => A;

/* ---- elements ---- */

/**
 * Adds an element to the desk.
 * @param key      the element's own position key
 * @param dflt     ({w, h, area}) => {x, y}   used when nothing is saved yet
 */
export function add(key, el, dflt) {
  el.dataset.id = key;
  el.classList.add("mv", "enter");
  if (el.dataset.rs && !el.querySelector(":scope > .grip")) {
    const grip = document.createElement("div");
    grip.className = "grip";
    el.appendChild(grip);
  }
  desk.appendChild(el);

  const saved = (Core.getState().pos || {})[key];
  if (el.dataset.rs && saved?.w) {
    el.style.width = saved.w + "px";
    el.style.height = saved.h + "px";
  }
  const { x, y } = saved
    ? { x: saved.fx * innerWidth, y: saved.fy * innerHeight }
    : dflt({ ...size(el), area: A });
  put(el, x, y);
}

export function remove(el) {
  el.remove();
}

/** True for the instant after an element was dragged, so its own click handler can ignore that click. */
export const wasDragged = el => el.dataset.moved === "1";

export function relayout() {
  A = Core.area();
  const pos = Core.getState().pos || {};
  for (const el of desk.querySelectorAll(".mv")) {
    const s = pos[el.dataset.id];
    if (s) put(el, s.fx * innerWidth, s.fy * innerHeight);
    else put(el, el.offsetLeft, el.offsetTop);
  }
  const hint = $("#hint");
  if (hint) hint.style.top = A.y + 26 + "px";
}

function savePosition(el) {
  Core.setPosition(el.dataset.id, {
    fx: el.offsetLeft / innerWidth,
    fy: el.offsetTop / innerHeight,
    w: el.dataset.rs ? el.offsetWidth : undefined,
    h: el.dataset.rs ? el.offsetHeight : undefined
  });
}

/* ---- dragging and resizing ---- */

function onDown(e) {
  const el = e.target.closest(".mv");
  if (!el) return;

  if (e.target.closest(".grip")) {
    drag = { el, mode: "resize", sx: e.clientX, sy: e.clientY, w: el.offsetWidth, h: el.offsetHeight };
    el.classList.add("dragging");
    e.preventDefault();
    return;
  }

  const arranging = document.body.classList.contains("arrange");
  if (!arranging && !e.target.closest("[data-drag]")) return;

  drag = {
    el,
    mode: "move",
    moved: false,
    sx: e.clientX,
    sy: e.clientY,
    dx: e.clientX - el.offsetLeft,
    dy: e.clientY - el.offsetTop
  };
  el.classList.add("dragging");
  el.dataset.moved = "";
  // No setPointerCapture on purpose: it retargets the click that follows to this element, which would stop a
  // widget's own click handler (the now-playing head, say) from ever firing. The window listeners are enough.
}

function onMove(e) {
  if (!drag) return;
  const { el } = drag;

  if (drag.mode === "resize") {
    el.style.width = Math.max(180, drag.w + (e.clientX - drag.sx)) + "px";
    el.style.height = Math.max(120, drag.h + (e.clientY - drag.sy)) + "px";
    return;
  }

  if (!drag.moved && Math.hypot(e.clientX - drag.sx, e.clientY - drag.sy) < 4) return;
  drag.moved = true;
  el.dataset.moved = "1";

  const { w, h } = size(el);
  let x = e.clientX - drag.dx;
  let y = e.clientY - drag.dy;

  let sx = null;
  let sy = null;
  const midX = A.x + (A.w - w) / 2;
  const midY = A.y + (A.h - h) / 2;
  for (const c of [A.x + MARGIN, midX, A.x + A.w - MARGIN - w]) if (Math.abs(x - c) < SNAP) { x = c; sx = c; }
  for (const c of [A.y + MARGIN, midY, A.y + A.h - MARGIN - h]) if (Math.abs(y - c) < SNAP) { y = c; sy = c; }
  gx.style.left = A.x + A.w / 2 + "px";
  gy.style.top = A.y + A.h / 2 + "px";
  gx.classList.toggle("on", sx !== null && Math.abs(sx - midX) < 1);
  gy.classList.toggle("on", sy !== null && Math.abs(sy - midY) < 1);
  put(el, x, y);
}

function onUp() {
  if (!drag) return;
  const { el, mode, moved } = drag;
  el.classList.remove("dragging");
  gx.classList.remove("on");
  gy.classList.remove("on");
  if (mode === "resize" || moved) savePosition(el);
  // the click that follows a drag must not trigger the element's own click handler
  setTimeout(() => { el.dataset.moved = ""; }, 0);
  drag = null;
}

/* ---- arrange mode ---- */

export function arrange(on) {
  document.body.classList.toggle("arrange", on);
}

export const isArranging = () => document.body.classList.contains("arrange");

export function resetLayout() {
  Core.clearPositions();
  Core.flush();
  location.reload();
}

/* ---- toast ---- */

export function toast(message) {
  const t = $("#toast");
  t.textContent = message;
  t.classList.add("show");
  clearTimeout(toast.h);
  toast.h = setTimeout(() => t.classList.remove("show"), 2200);
}

export function start() {
  desk = $("#desk");
  gx = $("#gx");
  gy = $("#gy");
  addEventListener("pointerdown", onDown);
  addEventListener("pointermove", onMove);
  addEventListener("pointerup", onUp);
  addEventListener("resize", relayout);
  $("#hDone").addEventListener("click", () => arrange(false));
  $("#hReset").addEventListener("click", resetLayout);
  relayout();
}
