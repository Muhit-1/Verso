/* ==================================================================
   VERSO — layout

   Makes the desk's panels draggable and resizable, and remembers
   where they were put and how big they were.

   Panels are marked with data-panel="<id>" in index.html — every
   loaded widget's wrapper carries the same attribute (see
   js/widgets.js). Nothing here knows what a dial or a task list is,
   only that an element carrying that attribute can be picked up,
   moved and resized.

   POSITIONS AND SIZES ARE STORED AS FRACTIONS of the viewport, not
   pixels. A desk laid out on a 4K monitor has to still make sense
   when the same settings file is read on a 1080p laptop, and pixels
   do not survive that. Everything is clamped on load so a panel can
   never be restored off-screen or resized to nothing.

   PERFORMANCE. Two rules, both learned from a real "dragging feels
   laggy" report:

   1. Never read a layout property (offsetHeight, getBoundingClientRect)
      in the same handler that just wrote one — that forces a
      synchronous reflow, and pointermove can fire well over 60 times
      a second on a precise mouse or a touchpad. Every size needed
      during a drag or resize is measured exactly once, at the start,
      and reused as plain numbers for the rest of the gesture. A
      fresh measurement is only taken once more, at the end, to save
      the final rectangle.
   2. Never write to the DOM more than once per frame. A drag or
      resize computes the next position/size on every pointermove but
      only actually applies it inside a requestAnimationFrame
      callback, coalesced the same way regions.publish() already
      coalesces its own postMessage calls.

   THE REGION
   In overlay mode the host clips its window to exactly the panel
   rectangles (see regions.js). A panel being dragged or resized is
   therefore only visible where the region already is — so the region
   has to follow along, frame by frame.
================================================================== */

import * as Host from "./host.js";
import * as Regions from "./regions.js";

const STORE_KEY = "verso.layout.v1";

/** Pixels to snap to. Small enough to feel free, large enough to line panels up. */
const SNAP = 8;

/**
 * How far the pointer must travel before a press becomes a drag.
 * Without it, every click on a panel nudges it by a pixel.
 */
const DRAG_THRESHOLD = 3;

const MIN_WIDTH = 140;
const MIN_HEIGHT = 90;

/**
 * Controls that must keep their own click behaviour. A press starting
 * on one of these never begins a drag — otherwise ticking a task or
 * pressing play would move the panel instead.
 */
const INTERACTIVE = "input, button, textarea, select, a, .task, .qrow";

/** @type {Record<string, {x:number, y:number, w:number, h?:number}>} */
let layout = {};

/* ---- persistence ------------------------------------------------ */

function readStored() {
  const host = Host.get();

  if (typeof host.getLayout === "function") {
    try {
      const value = host.getLayout();
      if (value && typeof value === "object") return value;
    } catch {
      /* fall through to local storage */
    }
  }

  try {
    const raw = localStorage.getItem(STORE_KEY);
    return raw ? JSON.parse(raw) : {};
  } catch {
    return {};
  }
}

function persist() {
  const host = Host.get();

  if (typeof host.saveLayout === "function") {
    try {
      host.saveLayout(layout);
    } catch {
      /* host offline — the local copy below is still written */
    }
  }

  try {
    localStorage.setItem(STORE_KEY, JSON.stringify(layout));
  } catch {
    /* storage disabled — the layout lives for this session only */
  }
}

function save(id, patch) {
  layout[id] = { ...layout[id], ...patch };
  persist();
}

/**
 * Fallback positions for a panel that has never been dragged or
 * resized, keyed by panel id. Registered by js/widgets.js before
 * {@link start} runs, once per mounted widget — this file has no
 * opinion of its own about where a dial or a task list belongs, only
 * about how a position, wherever it came from, gets applied and
 * persisted.
 *
 * Deliberately not written into `layout` itself: a default is what
 * renders when nothing is stored, not something to persist just for
 * having been shown once. {@link reset} relies on that distinction —
 * clearing to `{}` has to bring these back, not erase them too.
 */
const defaults = {};

/** Registers where a panel should render until the user moves it. */
export function registerDefault(id, pos) {
  defaults[id] = pos;
}

/* ---- applying --------------------------------------------------- */

const clamp = (value, min, max) => Math.min(Math.max(value, min), max);

/**
 * Positions (and, if a height was ever saved, sizes) one panel from
 * its stored fractions, clamping back into view. Width first: a panel
 * lifted out of the rail has no column to get its width from any
 * more, so it carries its own — and a widget's iframe fills whatever
 * height its wrapper ends up with, so an explicit height is only set
 * once the user has actually resized something.
 */
function place(el, pos) {
  const width = clamp(pos.w * window.innerWidth, MIN_WIDTH, window.innerWidth);

  el.style.position = "fixed";
  el.style.margin = "0";
  el.style.width = `${width}px`;

  if (pos.h) {
    const height = clamp(pos.h * window.innerHeight, MIN_HEIGHT, window.innerHeight);
    el.style.height = `${height}px`;
  }

  // Only after width/height are set — this is the one place a fresh
  // measurement is unavoidable, and it runs once per panel per
  // placement, not per pointer event.
  const height = el.offsetHeight;

  el.style.left = `${Math.round(clamp(pos.x * window.innerWidth, 0, Math.max(0, window.innerWidth - width)))}px`;
  el.style.top = `${Math.round(clamp(pos.y * window.innerHeight, 0, Math.max(0, window.innerHeight - height)))}px`;

  el.dataset.placed = "true";
}

/** Re-places every panel: its stored position if there is one, else its default. */
function applyAll() {
  for (const el of document.querySelectorAll("[data-panel]")) {
    const pos = layout[el.dataset.panel] ?? defaults[el.dataset.panel];
    if (pos) place(el, pos);
  }
}

/* ---- dragging --------------------------------------------------- */

function startDragging(el) {
  el.addEventListener("pointerdown", event => {
    // Left button only, never from a control with its own job, and
    // never from the resize grip — that gesture owns itself.
    if (event.button !== 0) return;
    if (event.target.closest(INTERACTIVE)) return;
    if (event.target.closest(".resize-grip")) return;

    const startX = event.clientX;
    const startY = event.clientY;

    // Measured once. Nothing below reads layout again until the next
    // gesture — see the perf note at the top of this file.
    const rect = el.getBoundingClientRect();
    const offsetX = startX - rect.left;
    const offsetY = startY - rect.top;
    const width = rect.width;
    const height = rect.height;

    let dragging = false;
    let frameScheduled = false;

    // The gesture's current answer, as plain numbers — not read back
    // from the DOM. A pending requestAnimationFrame write can still be
    // queued when the pointer is released (a fast flick can easily
    // outrun one frame), and getBoundingClientRect() at that moment
    // would read whatever the *last applied* frame left on screen, not
    // where the pointer actually is. These two variables are always
    // current; applyNow() below is what makes the DOM agree with them.
    let left = rect.left;
    let top = rect.top;

    const applyNow = () => {
      frameScheduled = false;
      el.style.left = `${left}px`;
      el.style.top = `${top}px`;
      Regions.publish();
    };

    const onMove = moveEvent => {
      if (!dragging) {
        const travelled = Math.hypot(moveEvent.clientX - startX, moveEvent.clientY - startY);
        if (travelled < DRAG_THRESHOLD) return;

        // Committed. Lift the panel out of the grid at exactly the size
        // it already occupies, so it does not jump on pickup.
        dragging = true;
        el.classList.add("dragging");
        el.style.position = "fixed";
        el.style.margin = "0";
        el.style.width = `${width}px`;
      }

      left = clamp(
        Math.round((moveEvent.clientX - offsetX) / SNAP) * SNAP,
        0, Math.max(0, window.innerWidth - width));
      top = clamp(
        Math.round((moveEvent.clientY - offsetY) / SNAP) * SNAP,
        0, Math.max(0, window.innerHeight - height));

      el.dataset.placed = "true";

      if (!frameScheduled) {
        frameScheduled = true;
        requestAnimationFrame(applyNow);
      }
    };

    const onUp = () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
      window.removeEventListener("pointercancel", onUp);

      if (!dragging) return;

      el.classList.remove("dragging");

      // Flush synchronously rather than trust a queued frame to have
      // already run — what gets saved below has to match what is
      // actually on screen right now.
      applyNow();

      save(el.dataset.panel, {
        x: left / window.innerWidth,
        y: top / window.innerHeight,
        w: width / window.innerWidth
      });

      Regions.publish();
    };

    // On the window, not the panel. A drag that tracks the cursor has to
    // keep receiving moves after the cursor has left the element it
    // started on — and the obvious alternative, setPointerCapture, throws
    // if the pointer is not currently active, which would take the whole
    // drag down with it.
    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
    window.addEventListener("pointercancel", onUp);
  });
}

/* ---- resizing ----------------------------------------------------- */

/**
 * A small grip in the bottom-right corner of the panel. Absolutely
 * positioned, so it paints above the widget's iframe regardless of
 * DOM order — a positioned element always stacks above a plain
 * in-flow one at the same z-index, which is what makes it grabbable
 * at all instead of the iframe swallowing the pointerdown.
 */
function startResizing(el) {
  const grip = document.createElement("div");
  grip.className = "resize-grip";
  grip.title = "Resize";
  el.appendChild(grip);

  grip.addEventListener("pointerdown", event => {
    if (event.button !== 0) return;

    // Never let this reach the drag handler above — dragging and
    // resizing must not both start from the same press.
    event.stopPropagation();

    const rect = el.getBoundingClientRect();
    const startX = event.clientX;
    const startY = event.clientY;
    const startWidth = rect.width;
    const startHeight = rect.height;
    const maxWidth = window.innerWidth - rect.left;
    const maxHeight = window.innerHeight - rect.top;

    el.classList.add("dragging"); // same "no transition while held" rule

    let width = startWidth;
    let height = startHeight;
    let frameScheduled = false;

    const applyNow = () => {
      frameScheduled = false;
      el.style.width = `${width}px`;
      el.style.height = `${height}px`;
      Regions.publish();
    };

    const onMove = moveEvent => {
      width = clamp(
        Math.round((startWidth + (moveEvent.clientX - startX)) / SNAP) * SNAP,
        MIN_WIDTH, maxWidth);
      height = clamp(
        Math.round((startHeight + (moveEvent.clientY - startY)) / SNAP) * SNAP,
        MIN_HEIGHT, maxHeight);

      if (!frameScheduled) {
        frameScheduled = true;
        requestAnimationFrame(applyNow);
      }
    };

    const onUp = () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
      window.removeEventListener("pointercancel", onUp);

      el.classList.remove("dragging");

      // Same flush-before-save rule as dragging, and the same reason:
      // a pending frame may not have run yet.
      applyNow();

      save(el.dataset.panel, {
        x: rect.left / window.innerWidth,
        y: rect.top / window.innerHeight,
        w: width / window.innerWidth,
        h: height / window.innerHeight
      });

      Regions.publish();
    };

    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
    window.addEventListener("pointercancel", onUp);
  });
}

/* ---- api -------------------------------------------------------- */

/**
 * Forgets every stored position/size and falls back to each panel's
 * default (see {@link registerDefault}) — there is no CSS composition
 * to return to any more now that every panel, built-in or loaded, is
 * placed the same way.
 */
export function reset() {
  layout = {};
  persist();
  applyAll();
  Regions.publish();
}

export function start() {
  layout = readStored() ?? {};

  applyAll();

  for (const el of document.querySelectorAll("[data-panel]")) {
    startDragging(el);
    startResizing(el);
  }

  // A resolution or DPI change moves the viewport under the panels.
  // Fractions are why this is a re-place rather than a rescue.
  window.addEventListener("resize", applyAll);
}
