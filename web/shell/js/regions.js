/* ==================================================================
   VERSO — regions

   Tells the host which rectangles the desk actually occupies, so it
   can reshape its window to exactly those and leave the rest of the
   desktop alone.

   This is what keeps the user's desktop usable. Everywhere outside
   the reported rectangles, the host window does not exist: the real
   wallpaper shows, the icons show, clicks and drags go straight
   through to Explorer. Inside them, the desk is a normal interactive
   page.

   Only overlay mode reports anything. In wallpaper mode the desk is
   the whole screen by definition, and a region would just be a
   full-screen rectangle.

   Rectangles are sent in CSS pixels; the host scales by the ratio it
   is told, because the page is the only side that knows its own
   devicePixelRatio.
================================================================== */

/**
 * Elements whose boxes make up the desk. Anything not listed here is
 * decoration that happens to sit inside one of these, or is not drawn
 * in overlay mode at all.
 */
const PANEL_SELECTOR = ".minute-track, .dial, .comp, .theme-toast.show";

/**
 * The region must match the painted box exactly. Any bleed exposes the
 * page background around each panel as a hairline, and any shortfall
 * clips the panel's own border. getBoundingClientRect already includes
 * the border, so rounding to the nearest device pixel is the whole job.
 */
const BLEED = 0;

let lastSignature = "";

function bridge() {
  return window.chrome?.webview ?? null;
}

function collect() {
  const scale = window.devicePixelRatio || 1;
  const radius = parseFloat(
    getComputedStyle(document.documentElement).getPropertyValue("--panel-radius")
  ) || 0;

  const panels = [];

  for (const el of document.querySelectorAll(PANEL_SELECTOR)) {
    const r = el.getBoundingClientRect();
    if (r.width <= 0 || r.height <= 0) continue;

    // The minute track spans the full width and has square ends; the
    // panels carry the theme's corner radius.
    const isTrack = el.classList.contains("minute-track");

    const left = Math.round((r.left - BLEED) * scale);
    const top = Math.round((r.top - BLEED) * scale);

    panels.push({
      x: left,
      y: top,
      // Derived from the rounded edges rather than the raw width, so two
      // adjacent panels cannot end up with a sub-pixel gap between them.
      w: Math.round((r.right + BLEED) * scale) - left,
      h: Math.round((r.bottom + BLEED) * scale) - top,
      r: isTrack ? 0 : Math.round(radius * scale)
    });
  }

  return panels;
}

/**
 * Sends the current geometry, but only when it has actually changed.
 * Reshaping a window forces a repaint of everything underneath it, so
 * doing it on every animation frame would make the desktop flicker.
 */
export function publish() {
  const web = bridge();
  if (!web) return;

  const panels = collect();
  const signature = JSON.stringify(panels);
  if (signature === lastSignature) return;
  lastSignature = signature;

  web.postMessage({ type: "verso:regions", panels });
}

/**
 * Watches everything that can move a panel: the window resizing, the
 * task list growing, a theme changing the corner radius or the type
 * metrics, and the toast appearing.
 */
export function start() {
  if (!bridge()) return;

  publish();

  const observer = new ResizeObserver(publish);
  for (const el of document.querySelectorAll(PANEL_SELECTOR)) {
    observer.observe(el);
  }

  // Panels are added and removed (the toast), and their contents change
  // height (tasks, the queue), neither of which a ResizeObserver on the
  // original set would catch.
  new MutationObserver(() => {
    for (const el of document.querySelectorAll(PANEL_SELECTOR)) {
      observer.observe(el);
    }
    publish();
  }).observe(document.body, { childList: true, subtree: true, attributes: true });

  window.addEventListener("resize", publish);

  // The host asks for the geometry again after resuming from a pause,
  // because a resume is not a resize and nothing else would fire.
  window.chrome.webview.addEventListener("message", ev => {
    if (ev.data?.type === "verso:republish") {
      lastSignature = "";
      publish();
    }
  });

  // A webfont arriving after first paint resizes the dial, and a theme
  // swap changes the radius. Neither fires resize.
  document.fonts?.ready.then(publish);
}
