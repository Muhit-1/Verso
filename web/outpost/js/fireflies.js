/* ==================================================================
   VERSO — fireflies

   Slow lights drifting over the wallpaper. Built to be cheap, because an animated layer over a playing video is not
   free (measured: the original 35-light, full-resolution, 24 fps version cost ~20 points of CPU on top of the video):
   - 18 lights, ~15 fps, drawn on a canvas at HALF resolution and scaled up by CSS (the glow is soft anyway);
   - one pre-rendered glow sprite per palette colour (no per-frame shadowBlur);
   - a real off switch (hint pill in arrange mode): when off there is no canvas and no loop at all.
   The host suspends this whole page when the desk is paused, so nothing runs then either.
================================================================== */

const COUNT = 18;
const FRAME_MS = 1000 / 15;
const SCALE = 0.5;

let canvas;
let ctx;
let flies = [];
let running = false;
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

function resize() {
  canvas.width = Math.round(innerWidth * SCALE);
  canvas.height = Math.round(innerHeight * SCALE);
}

export function init() {
  canvas = document.getElementById("flies");
  ctx = canvas.getContext("2d");
  addEventListener("resize", () => { if (running) resize(); });
  flies = Array.from({ length: COUNT }, () => ({
    x: Math.random(), y: Math.random(),
    r: Math.random() * 2.5 + 1.5,
    vx: (Math.random() - 0.5) * 0.0004,
    vy: (Math.random() - 0.5) * 0.0003,
    o: Math.random() * 0.6,
    to: Math.random() * 0.6 + 0.2
  }));
}

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

export function setEnabled(on) {
  if (on === running) return;
  running = on;
  canvas.style.display = on ? "" : "none";
  if (on) {
    resize();
    requestAnimationFrame(frame);
  } else {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
  }
}
