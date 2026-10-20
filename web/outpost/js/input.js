/* ==================================================================
   VERSO — Outpost input

   This page lives BEHIND the desktop icons, so the host mirrors the real mouse into it
   (src/Verso.Interop/InputMirror.cs). Two things do not survive that trip and are rebuilt here —
   both measured by tools/Verso.Spike.Layers --input:

   1. HOVER. Windows keeps ending it (195 enter/leave pairs in 30 s of normal mouse use) because
      the real cursor is not over this page, so :hover / mouseenter / mouseleave cannot be trusted.
      Hover is derived from pointer coordinates instead and shown as the .hv class.
   2. THE WHEEL. A posted WM_MOUSEWHEEL never arrives. The host calls window.__verso.wheel(...)
      with the position in physical pixels and the raw delta (120 per notch, positive = away).
================================================================== */

const HOVERABLE = "button, .chip, .btn, .rbtn, .mi, .tile, .swatch";

let hovered = null;
let pending = null;

function setHover(el) {
  if (el === hovered) return;
  hovered?.classList.remove("hv");
  hovered = el;
  hovered?.classList.add("hv");
}

function resolveHover() {
  pending = null;
  if (document.body.classList.contains("arrange")) {
    setHover(null);
    return;
  }
  const el = document.elementFromPoint(lastX, lastY);
  setHover(el ? el.closest(HOVERABLE) : null);
}

let lastX = -1;
let lastY = -1;

export function start() {
  addEventListener("pointermove", e => {
    lastX = e.clientX;
    lastY = e.clientY;
    pending ??= requestAnimationFrame(resolveHover);
  });

  window.__verso = {
    /** The pointer left the bare desktop. */
    leave() {
      setHover(null);
      window.__verso.emit?.("pointerLeft");
    },

    /** A wheel notch at physical (x, y). Gives the page the event, and scrolls if nobody objects. */
    wheel(x, y, delta) {
      const dpr = window.devicePixelRatio || 1;
      const cx = x / dpr;
      const cy = y / dpr;
      const target = document.elementFromPoint(cx, cy);
      if (!target) return;

      const dy = -delta; // WM_MOUSEWHEEL: positive = up; DOM deltaY: positive = down
      const ev = new WheelEvent("wheel", { deltaY: dy, clientX: cx, clientY: cy, bubbles: true, cancelable: true });
      if (!target.dispatchEvent(ev)) return;

      for (let n = target; n && n !== document.body; n = n.parentElement) {
        if (n.scrollHeight > n.clientHeight + 1 && getComputedStyle(n).overflowY !== "hidden") {
          n.scrollTop += dy;
          break;
        }
      }
    }
  };
}
