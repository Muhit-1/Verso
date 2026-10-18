/* Clock — the big clock and date. Its only setting (12/24 hour) is its own: click AM/PM. */

import { register } from "../framework.js";
import { wasDragged } from "../layout.js";

const pad2 = n => String(n).padStart(2, "0");

register({
  id: "clock",

  mount(ctx) {
    const el = document.createElement("section");
    el.className = "clock";
    el.innerHTML = `
      <div class="time"><span class="h">00</span><span class="sep">:</span><span class="m">00</span><span class="ampm" title="Click to switch 12/24 hour">AM</span></div>
      <div class="date"></div>`;

    const h = el.querySelector(".h");
    const m = el.querySelector(".m");
    const ap = el.querySelector(".ampm");
    const date = el.querySelector(".date");
    const set = (node, text) => { if (node.textContent !== text) node.textContent = text; };

    const tick = () => {
      const is24 = !!ctx.state.get().is24;
      const now = new Date();
      let hours = now.getHours();
      const suffix = hours >= 12 ? "PM" : "AM";
      if (!is24) hours = hours % 12 || 12;
      set(h, pad2(hours));
      set(m, pad2(now.getMinutes()));
      ap.style.display = is24 ? "none" : "";
      set(ap, suffix);
      set(date, now.toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric" }));
    };

    ap.addEventListener("click", () => {
      if (wasDragged(el)) return;
      ctx.state.patch({ is24: !ctx.state.get().is24 });
      tick();
    });

    ctx.mount(el, ({ h: height, area }) => ({ x: area.x + 48, y: area.y + area.h - 36 - height }));
    tick();
    ctx.every(1000, tick);
  }
});
