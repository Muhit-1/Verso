/* ==================================================================
   VERSO DASHBOARD — title bar

   The window has no OS title bar (see DashboardWindow.xaml.cs) — this
   is what makes the drawn one in web/dashboard/index.html actually
   move, minimize, maximize and close the real window. Every action is
   a WebMessage; DashboardWindow.OnWebMessage is what carries it out
   natively. In a plain browser there is no chrome.webview at all, so
   every call here is a harmless no-op — the title bar still renders,
   it just cannot move a window that isn't really there.
================================================================== */

function post(action) {
  window.chrome?.webview?.postMessage({ type: "verso:window", action });
}

export function start() {
  const dragRegion = document.getElementById("titlebarDrag");
  const minimizeBtn = document.getElementById("titlebarMinimize");
  const maximizeBtn = document.getElementById("titlebarMaximize");
  const closeBtn = document.getElementById("titlebarClose");

  dragRegion?.addEventListener("pointerdown", ev => {
    // Only the primary button, and never when the click landed on one of
    // the control buttons themselves — they sit inside the same region.
    if (ev.button !== 0 || ev.target.closest("[data-titlebar-btn]")) return;
    post("drag");
  });

  dragRegion?.addEventListener("dblclick", ev => {
    if (ev.target.closest("[data-titlebar-btn]")) return;
    maximizeBtn?.click();
  });

  minimizeBtn?.addEventListener("click", () => post("minimize"));
  maximizeBtn?.addEventListener("click", () => {
    maximizeBtn.classList.toggle("is-restored");
    post("maximize");
  });
  closeBtn?.addEventListener("click", () => post("close"));
}
