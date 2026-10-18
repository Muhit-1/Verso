/* ==================================================================
   Fireflies — slow lights drifting over the wallpaper. An ambience widget: no panel, just a canvas.

   Built to be cheap (an animated layer over a playing video is not free — the first version cost ~20 points of
   CPU): 18 lights, ~15 fps, a half-resolution canvas scaled up by CSS, one pre-rendered glow sprite per palette
   colour (no per-frame shadowBlur). Turned off, the canvas and the loop do not exist at all. The host suspends
   the whole page when the desk is paused, so nothing runs then either.
================================================================== */

import { register } from "../framework.js";

const COUNT = 18;
const FRAME_MS = 1000 / 15;
const SCALE = 0.5;

register({
  id: "flies",

  mount() {
    const canvas = document.createElement("canvas");
    canvas.id = "flies";
    document.body.appendChild(canvas);
    const ctx = canvas.getContext("2d");

    const resize = () => {
      canvas.width = Math.round(innerWidth * SCALE);
      canvas.height = Math.round(innerHeight * SCALE);
    };
    resize();
    addEventListener("resize", resize);

    const flies = Array.from({ length: COUNT }, () => ({
      x: Math.random(), y: Math.random(),
      r: Math.random() * 2.5 + 1.5,
      vx: (Math.random() - 0.5) * 0.0004,
      vy: (Math.random() - 0.5) * 0.0003,
      o: Math.random() * 0.6,
      to: Math.random() * 0.6 + 0.2
    }));

    let sprite = null;
    let spriteKey = "";
    function glow() {
      const rgb = getComputedStyle(document.body).getPropertyValue("--fly").trim() || "255,180,100";
      if (rgb === spriteKey) return sprite;
      spriteKey = rgb;
      sprite = document.createElement("canvas");
      sprite.width = sprite.height = 32;
      const g = sprite.getContext("2d");
      const grad = g.createRadialGradient(16, 16, 0, 16, 16, 16);
      grad.addColorStop(0, `rgba(${rgb},1)`);
      grad.addColorStop(0.3, `rgba(${rgb},.5)`);
      grad.addColorStop(1, `rgba(${rgb},0)`);
      g.fillStyle = grad;
      g.fillRect(0, 0, 32, 32);
      return sprite;
    }

    let running = true;
    let last = 0;
    function frame(t) {
      if (!running) return;
      requestAnimationFrame(frame);
      if (t - last < FRAME_MS) return;
      last = t;
      const w = canvas.width;
      const h = canvas.height;
      ctx.clearRect(0, 0, w, h);
      const s = glow();
      for (const f of flies) {
        f.x += f.vx * 4;
        f.y += f.vy * 4;
        f.o += (f.to - f.o) * 0.02;
        if (f.x < 0 || f.x > 1) f.vx *= -1;
        if (f.y < 0 || f.y > 1) f.vy *= -1;
        const size = f.r * 9 * SCALE * 1.6;
        ctx.globalAlpha = f.o;
        ctx.drawImage(s, f.x * w - size / 2, f.y * h - size / 2, size, size);
      }
      ctx.globalAlpha = 1;
    }
    requestAnimationFrame(frame);

    return {
      dispose() {
        running = false;
        removeEventListener("resize", resize);
        canvas.remove();
      }
    };
  }
});
