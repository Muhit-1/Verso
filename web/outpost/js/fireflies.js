/* ==================================================================
   VERSO — fireflies

   35 slow lights drifting over the wallpaper. Cheap on purpose: one pre-rendered glow sprite per
   palette colour (no per-frame shadowBlur, which is what makes canvas glows expensive), drawn at
   ~24 fps. The host suspends this whole page when the desk is paused, so nothing runs then.
================================================================== */

const COUNT = 35;
const FRAME_MS = 1000 / 24;

export function start() {
  const canvas = document.getElementById("flies");
  const ctx = canvas.getContext("2d");
  let w = 0;
  let h = 0;
  const resize = () => { w = canvas.width = innerWidth; h = canvas.height = innerHeight; };
  resize();
  addEventListener("resize", resize);

  const flies = Array.from({ length: COUNT }, () => ({
    x: Math.random() * innerWidth,
    y: Math.random() * innerHeight,
    r: Math.random() * 2.5 + 1,
    vx: (Math.random() - 0.5) * 0.7,
    vy: (Math.random() - 0.5) * 0.5,
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
    sprite.width = sprite.height = 48;
    const g = sprite.getContext("2d");
    const grad = g.createRadialGradient(24, 24, 0, 24, 24, 24);
    grad.addColorStop(0, `rgba(${rgb},1)`);
    grad.addColorStop(0.25, `rgba(${rgb},.55)`);
    grad.addColorStop(1, `rgba(${rgb},0)`);
    g.fillStyle = grad;
    g.fillRect(0, 0, 48, 48);
    return sprite;
  }

  let last = 0;
  function frame(t) {
    requestAnimationFrame(frame);
    if (t - last < FRAME_MS) return;
    last = t;
    ctx.clearRect(0, 0, w, h);
    const s = glow();
    for (const f of flies) {
      f.x += f.vx;
      f.y += f.vy;
      f.o += (f.to - f.o) * 0.012;
      if (f.x < 0 || f.x > w) f.vx *= -1;
      if (f.y < 0 || f.y > h) f.vy *= -1;
      const size = f.r * 9;
      ctx.globalAlpha = f.o;
      ctx.drawImage(s, f.x - size / 2, f.y - size / 2, size, size);
    }
    ctx.globalAlpha = 1;
  }
  requestAnimationFrame(frame);
}
