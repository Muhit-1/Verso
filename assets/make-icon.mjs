/*
 * Generates the Verso mark: assets/verso.png and assets/verso.ico
 *
 * The mark is the minute track from the dashboard, reduced to three strokes —
 * two spent brass minutes and the lit one. It reads at 16px, which is the only
 * size a tray icon is ever seen at.
 *
 * No dependencies: PNG is written with Node's built-in zlib, and an .ico is
 * just a small header wrapped around PNG payloads (Vista and later).
 *
 *   node assets/make-icon.mjs
 */
import { deflateSync } from "node:zlib";
import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));

// ---- palette, lifted from web/themes/smoked-petrol/theme.css ----
const INK       = [0x0b, 0x14, 0x16];
const INK_LIFT  = [0x12, 0x22, 0x2a];
const LUME      = [0x9f, 0xe0, 0xc4];
const BRASS     = [0xc9, 0xa9, 0x6a];

const mix = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * t));

function render(size) {
  const px = new Uint8Array(size * size * 4); // RGBA
  const r = size * 0.22;                      // corner radius
  const cx = (size - 1) / 2;
  const cy = (size - 1) / 2;

  // Signed distance to a rounded square, used for both the fill and the
  // antialiased edge. Cheaper than drawing a path and exact at every size.
  const sdf = (x, y) => {
    const dx = Math.abs(x - cx) - (size / 2 - r) + 0.5;
    const dy = Math.abs(y - cy) - (size / 2 - r) + 0.5;
    const ox = Math.max(dx, 0);
    const oy = Math.max(dy, 0);
    return Math.min(Math.max(dx, dy), 0) + Math.hypot(ox, oy) - r;
  };

  const put = (x, y, rgb, a) => {
    if (a <= 0) return;
    const i = (y * size + x) * 4;
    const prev = px[i + 3] / 255;
    const out = a + prev * (1 - a);
    for (let c = 0; c < 3; c++) {
      px[i + c] = Math.round((rgb[c] * a + px[i + c] * prev * (1 - a)) / (out || 1));
    }
    px[i + 3] = Math.round(out * 255);
  };

  // ground: a lifted centre falling off to ink, so it does not read as flat
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const d = sdf(x, y);
      const cover = Math.min(1, Math.max(0, 0.5 - d));
      if (cover <= 0) continue;
      const falloff = Math.min(1, Math.hypot(x - cx, y - cy) / (size * 0.62));
      put(x, y, mix(INK_LIFT, INK, falloff), cover);
    }
  }

  // brass hairline just inside the edge
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const d = sdf(x, y);
      const w = Math.max(1, size / 64);
      const on = Math.min(1, Math.max(0, 1 - Math.abs(d + w * 0.9) / w));
      if (on > 0) put(x, y, BRASS, on * 0.30);
    }
  }

  // three ticks: spent, now, to come
  const bar = (fx, halfH, halfW, rgb, alpha) => {
    const x0 = cx + fx * size;
    for (let y = Math.round(cy - halfH); y <= Math.round(cy + halfH); y++) {
      for (let x = Math.round(x0 - halfW - 1); x <= Math.round(x0 + halfW + 1); x++) {
        if (x < 0 || y < 0 || x >= size || y >= size) continue;
        const cover = Math.min(1, Math.max(0, halfW + 0.5 - Math.abs(x - x0)));
        put(x, y, rgb, cover * alpha);
      }
    }
  };

  const unit = size / 32;

  // The lit minute glows before it is drawn, so the bloom sits underneath it.
  // Distance to the bar as a capsule, then an exponential falloff - a stack of
  // wider rectangles reads as steps at every size above 32px.
  const glowHalfH = size * 0.26;
  const glowHalfW = Math.max(0.6, unit * 1.35);
  const reach = size * 0.16;
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      if (sdf(x, y) > 0) continue; // never bloom past the rounded edge
      const dx = Math.max(0, Math.abs(x - cx) - glowHalfW);
      const dy = Math.max(0, Math.abs(y - cy) - glowHalfH);
      const d = Math.hypot(dx, dy);
      const a = Math.exp(-(d / reach) * 2.4) * 0.34;
      if (a > 0.004) put(x, y, LUME, a);
    }
  }

  // two spent minutes either side, thin enough to stay hairlines at 16px
  bar(-0.185, size * 0.115, Math.max(0.5, unit * 0.62), BRASS, 0.62);
  bar( 0.185, size * 0.115, Math.max(0.5, unit * 0.62), BRASS, 0.26);

  bar(0, glowHalfH, glowHalfW, LUME, 1);

  return px;
}

// ---- minimal PNG writer ----
const CRC = (() => {
  const t = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c;
  }
  return t;
})();

function crc32(buf) {
  let c = -1;
  for (const b of buf) c = CRC[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ -1) >>> 0;
}

function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, "latin1"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([len, body, crc]);
}

function png(size, rgba) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8;   // bit depth
  ihdr[9] = 6;   // truecolour + alpha
  const raw = Buffer.alloc(size * (size * 4 + 1));
  for (let y = 0; y < size; y++) {
    raw[y * (size * 4 + 1)] = 0; // filter: none
    Buffer.from(rgba.buffer, y * size * 4, size * 4)
      .copy(raw, y * (size * 4 + 1) + 1);
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr),
    chunk("IDAT", deflateSync(raw, { level: 9 })),
    chunk("IEND", Buffer.alloc(0))
  ]);
}

function ico(entries) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);              // type: icon
  header.writeUInt16LE(entries.length, 4);

  let offset = 6 + entries.length * 16;
  const dir = [];
  for (const { size, data } of entries) {
    const e = Buffer.alloc(16);
    e[0] = size >= 256 ? 0 : size;         // 0 means 256
    e[1] = size >= 256 ? 0 : size;
    e.writeUInt16LE(1, 4);                 // colour planes
    e.writeUInt16LE(32, 6);                // bits per pixel
    e.writeUInt32LE(data.length, 8);
    e.writeUInt32LE(offset, 12);
    offset += data.length;
    dir.push(e);
  }
  return Buffer.concat([header, ...dir, ...entries.map(e => e.data)]);
}

const sizes = [16, 24, 32, 48, 64, 128, 256];
const built = sizes.map(size => ({ size, data: png(size, render(size)) }));

writeFileSync(join(HERE, "verso.png"), built.at(-1).data);
writeFileSync(join(HERE, "verso.ico"), ico(built));

// The web layer gets its own copy rather than reaching outside the web root,
// which the native host serves as the only readable folder.
writeFileSync(join(HERE, "..", "web", "shell", "favicon.png"),
  built.find(b => b.size === 128).data);

console.log(`verso.png          ${built.at(-1).data.length} bytes`);
console.log(`verso.ico          ${sizes.join(", ")}`);
console.log(`web/shell/favicon.png  128`);
