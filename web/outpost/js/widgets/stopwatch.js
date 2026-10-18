/* Stopwatch — laps included. A card of its own, independent of the Pomodoro and everything else. */

import { register } from "../framework.js";

const pad2 = n => String(n).padStart(2, "0");
const html = ms => {
  const c = Math.floor(ms / 10) % 100;
  const s = Math.floor(ms / 1000) % 60;
  const m = Math.floor(ms / 60000);
  return `${pad2(m)}:${pad2(s)}<span class="cs">.${pad2(c)}</span>`;
};

register({
  id: "sw",

  mount(ctx) {
    const st = { t: 0, run: false, timer: null, last: 0 };
    const el = document.createElement("div");
    el.className = "card glass";
    el.innerHTML = `
      <h3 data-drag><span>Stopwatch</span><small>go on, I'll wait</small></h3>
      <div class="swd"><div class="disp">00:00<span class="cs">.00</span></div></div>
      <div class="row"><button class="btn primary go">Start</button><button class="btn lap">Lap</button><button class="btn rs">Reset</button></div>
      <div class="laps"></div>`;

    const q = s => el.querySelector(s);

    q(".go").addEventListener("click", () => {
      st.run = !st.run;
      q(".go").textContent = st.run ? "Stop" : "Start";
      ctx.clear(st.timer);
      if (st.run) {
        const t0 = performance.now() - st.t;
        st.timer = ctx.every(50, () => {
          st.t = performance.now() - t0;
          q(".disp").innerHTML = html(st.t);
        });
      }
    });

    q(".lap").addEventListener("click", () => {
      if (!st.t) return;
      const row = document.createElement("div");
      row.innerHTML = `<span>Lap ${q(".laps").children.length + 1}</span><span>${html(st.t - st.last).replace(/<[^>]+>/g, "")}</span>`;
      st.last = st.t;
      q(".laps").prepend(row);
    });

    q(".rs").addEventListener("click", () => {
      st.run = false;
      ctx.clear(st.timer);
      st.t = 0;
      st.last = 0;
      q(".go").textContent = "Start";
      q(".disp").innerHTML = html(0);
      q(".laps").innerHTML = "";
    });

    ctx.mount(el, ({ area }) => ({ x: area.x + area.w / 2 + 10, y: area.y + 330 }));
  }
});
