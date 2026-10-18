/* Pomodoro — a focus timer. A card of its own: it shares nothing with the stopwatch or anything else. */

import { register } from "../framework.js";

const f2 = s => String(Math.floor(s / 60)).padStart(2, "0") + ":" + String(s % 60).padStart(2, "0");

register({
  id: "pomo",

  mount(ctx) {
    const st = { f: 25, b: 5, left: 25 * 60, run: false, brk: false, timer: null, sessions: 0 };
    const el = document.createElement("div");
    el.className = "card glass";
    el.innerHTML = `
      <h3 data-drag><span>Pomodoro</span><small class="lab">focus session</small></h3>
      <div class="chips">
        <button class="chip on" data-f="25" data-b="5">25 / 5</button><button class="chip" data-f="45" data-b="5">45 / 5</button>
        <button class="chip" data-f="50" data-b="10">50 / 10</button><button class="chip" data-f="90" data-b="15">90 / 15</button>
      </div>
      <div class="ring"><svg viewBox="0 0 100 100"><circle class="tr0" cx="50" cy="50" r="42"/><circle class="fg" cx="50" cy="50" r="42"/></svg>
        <div class="big"><div class="disp">25:00</div><div class="sub">Focus</div></div></div>
      <div class="row"><button class="btn primary go">Start</button><button class="btn rs">Reset</button></div>
      <div class="dots"><i></i><i></i><i></i><i></i></div>`;

    const q = s => el.querySelector(s);
    const set = (node, text) => { if (node.textContent !== text) node.textContent = text; };

    function draw() {
      const total = (st.brk ? st.b : st.f) * 60;
      set(q(".disp"), f2(st.left));
      q(".fg").style.strokeDashoffset = 263.89 * (1 - st.left / total);
      set(q(".sub"), st.brk ? "Break" : "Focus");
      set(q(".lab"), st.brk ? "take five" : "focus session");
      set(q(".go"), st.run ? "Pause" : "Start");
      const done = st.sessions === 0 ? 0 : ((st.sessions - 1) % 4) + 1;
      el.querySelectorAll(".dots i").forEach((dot, i) => dot.classList.toggle("d", i < done));
    }

    function step() {
      if (--st.left > 0) { draw(); return; }
      if (!st.brk) st.sessions++;
      st.brk = !st.brk;
      st.left = (st.brk ? st.b : st.f) * 60;
      ctx.announce(st.brk ? "Focus complete" : "Break over");
      draw();
    }

    function reset() {
      st.run = false;
      ctx.clear(st.timer);
      st.brk = false;
      st.left = st.f * 60;
      draw();
    }

    q(".go").addEventListener("click", () => {
      st.run = !st.run;
      ctx.clear(st.timer);
      if (st.run) st.timer = ctx.every(1000, step);
      draw();
    });
    q(".rs").addEventListener("click", reset);
    el.querySelectorAll(".chip").forEach(c => c.addEventListener("click", () => {
      el.querySelectorAll(".chip").forEach(x => x.classList.remove("on"));
      c.classList.add("on");
      st.f = +c.dataset.f;
      st.b = +c.dataset.b;
      reset();
    }));

    // default: left of centre, so Pomodoro and Stopwatch sit side by side when both are on —
    // a placement default only; neither knows the other exists.
    ctx.mount(el, ({ area }) => ({ x: area.x + area.w / 2 - 310, y: area.y + 330 }));
    draw();
  }
});
