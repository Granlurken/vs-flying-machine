// Tegner urskivene til instrumentbordet → Wright/assets/wright/textures/dash/*.png
// Kjør: node tools/dash_textures.js
// Skivene er 64 px: messingring, gulnet papirskive, streker og tall i en 3×5-pikselskrift.
// Vinkel 0 = rett opp, positive grader = med klokka (slik piloten ser det).
const fs = require("fs");
const path = require("path");
const zlib = require("zlib");

const S = 64, C = (S - 1) / 2;
const outDir = path.join(__dirname, "..", "Wright", "assets", "wright", "textures", "dash");
fs.mkdirSync(outDir, { recursive: true });

// ---------- PNG (RGBA)
function crc32(buf) {
  let c, crc = ~0;
  for (let n = 0; n < buf.length; n++) { c = (crc ^ buf[n]) & 255; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; crc = (crc >>> 8) ^ c; }
  return ~crc >>> 0;
}
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
function writePng(file, w, h, rgba) {
  const raw = Buffer.alloc((w * 4 + 1) * h);
  for (let y = 0; y < h; y++) rgba.copy(raw, y * (w * 4 + 1) + 1, y * w * 4, (y + 1) * w * 4);
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 6;
  fs.writeFileSync(file, Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]));
}

// Fast frø, så skivene blir like hver gang
let seed = 1911;
const rnd = () => { seed = (seed + 0x6D2B79F5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };

const INK = [38, 32, 26], RED = [150, 36, 28];

function canvas() {
  const px = Buffer.alloc(S * S * 4);
  const set = (x, y, [r, g, b], a = 255) => {
    x = Math.round(x); y = Math.round(y);
    if (x < 0 || y < 0 || x >= S || y >= S) return;
    const o = (y * S + x) * 4; px[o] = r; px[o + 1] = g; px[o + 2] = b; px[o + 3] = a;
  };
  return { px, set };
}

// Messingring + papirskive med flekker og mørkere kant (gammel og gulnet)
function dialBase(cv, face = [226, 214, 180]) {
  for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) {
    const d = Math.hypot(x - C, y - C);
    if (d > 31.5) continue;
    if (d > 27.5) {
      // Ringen: lysere oppe til venstre, som om lyset kommer derfra
      const lit = ((C - x) + (C - y)) / (2 * S) + 0.5;
      const k = 0.75 + 0.45 * lit + (rnd() - 0.5) * 0.12;
      cv.set(x, y, [168 * k, 128 * k, 62 * k].map(v => Math.min(255, v)));
    } else if (d > 26.5) {
      cv.set(x, y, [70, 52, 30]);
    } else {
      const edge = Math.max(0, (d - 18) / 9) * 0.18;
      const k = 1 - edge - rnd() * 0.06 - (rnd() < 0.03 ? 0.1 : 0);
      cv.set(x, y, face.map(v => v * k));
    }
  }
}

// Strek fra radius r0 til r1 i vinkel a (grader, 0 = opp, med klokka)
function tick(cv, a, r0, r1, col = INK, w = 1) {
  const t = a * Math.PI / 180, dx = Math.sin(t), dy = -Math.cos(t);
  for (let r = r0; r <= r1; r += 0.25) for (let o = 0; o < w; o++) {
    const nx = -dy * (o - (w - 1) / 2), ny = dx * (o - (w - 1) / 2);
    cv.set(C + dx * r + nx, C + dy * r + ny, col);
  }
}

// 3×5-skrift (W og M er 5 brede)
const FONT = {
  0: ["111", "101", "101", "101", "111"], 1: ["010", "110", "010", "010", "111"], 2: ["111", "001", "111", "100", "111"],
  3: ["111", "001", "011", "001", "111"], 4: ["101", "101", "111", "001", "001"], 5: ["111", "100", "111", "001", "111"],
  6: ["111", "100", "111", "101", "111"], 7: ["111", "001", "010", "010", "010"], 8: ["111", "101", "111", "101", "111"],
  9: ["111", "101", "111", "001", "111"],
  A: ["010", "101", "111", "101", "101"], D: ["110", "101", "101", "101", "110"], E: ["111", "100", "110", "100", "111"],
  F: ["111", "100", "110", "100", "100"], L: ["100", "100", "100", "100", "111"], N: ["101", "111", "111", "111", "101"],
  P: ["110", "101", "110", "100", "100"], S: ["011", "100", "010", "001", "110"], T: ["111", "010", "010", "010", "010"],
  U: ["101", "101", "101", "101", "111"], W: ["10001", "10001", "10101", "10101", "01010"], "/": ["001", "001", "010", "100", "100"]
};
function text(cv, str, cx, cy, col = INK) {
  const glyphs = [...String(str)].map(ch => FONT[ch]);
  const width = glyphs.reduce((s, g) => s + g[0].length + 1, -1);
  let x = Math.round(cx - width / 2);
  const y = Math.round(cy - 2);
  for (const g of glyphs) {
    g.forEach((row, j) => [...row].forEach((b, i) => { if (b === "1") cv.set(x + i, y + j, col); }));
    x += g[0].length + 1;
  }
}
const at = (a, r) => { const t = a * Math.PI / 180; return [C + Math.sin(t) * r, C - Math.cos(t) * r]; };

// Skala: verdier v0..v1 fordelt på vinklene a0..a1
function scale(cv, { a0, a1, v0, v1, major, minor, labels, labelR = 19 }) {
  const ang = v => a0 + (a1 - a0) * (v - v0) / (v1 - v0);
  for (let v = v0; v <= v1 + 1e-9; v += minor) tick(cv, ang(v), 23.5, 26, INK);
  for (let v = v0; v <= v1 + 1e-9; v += major) tick(cv, ang(v), 21.5, 26, INK, 2);
  for (const [v, s] of labels) text(cv, s, ...at(ang(v), labelR));
}

const dials = {
  // Fart i blokker/sekund, 0–25 over 270°
  speed(cv) {
    dialBase(cv);
    scale(cv, { a0: -135, a1: 135, v0: 0, v1: 25, major: 5, minor: 2.5, labels: [[0, "0"], [5, "5"], [10, "10"], [15, "15"], [20, "20"], [25, "25"]] });
    text(cv, "SPEED", C, C + 8);
  },
  // Høydemåler: lang viser = 100 blokker per runde, kort = 1000. Tallene er tiere / hundrere
  altimeter(cv) {
    dialBase(cv);
    for (let i = 0; i < 50; i++) tick(cv, i * 7.2, i % 5 ? 24 : 22, 26, INK, i % 5 ? 1 : 2);
    for (let i = 0; i < 10; i++) text(cv, i, ...at(i * 36, 18.5));
    text(cv, "ALT", C, C + 9);
  },
  // Drivstoff: E–F over en halvsirkel øverst
  fuel(cv) {
    dialBase(cv);
    scale(cv, { a0: -90, a1: 90, v0: 0, v1: 1, major: 0.5, minor: 0.25, labels: [[0, "E"], [1, "F"]], labelR: 18 });
    // Rødt felt nederst på skalaen (under en åttendedel igjen)
    for (let a = -90; a <= -67.5; a += 1.5) tick(cv, a, 24.5, 26, RED);
    text(cv, "1/2", ...at(0, 17.5));
    text(cv, "FUEL", C, C + 10);
  },
  // Kompass: skiva selv dreier (kortet), N peker nord. Fast rød strek oppe i ringen viser kursen
  compass(cv) {
    dialBase(cv, [206, 196, 166]);
    for (let i = 0; i < 36; i++) tick(cv, i * 10, i % 9 ? (i % 3 ? 24.5 : 23) : 21, 26, INK, i % 9 ? 1 : 2);
    [["N", 0], ["E", 90], ["S", 180], ["W", 270]].forEach(([s, a]) => text(cv, s, ...at(a, 17.5), s === "N" ? RED : INK));
    // Liten rød pil ved N
    for (let k = 0; k < 4; k++) for (let j = -k; j <= k; j++) cv.set(C + j, C - 13 + k, RED);
  },
  // Lampeplate: mørk malt plate, STALL og FUEL under de to lampene (lampene sitter 0.045 blokk fra midten, 0.02 over)
  lamps(cv) {
    dialBase(cv, [52, 46, 40]);
    const CREAM = [214, 200, 164];
    text(cv, "STALL", C - 14.4, C + 9, CREAM);
    text(cv, "FUEL", C + 14.4, C + 9, CREAM);
  },
  // Tom plass: mørkt hull med to skruer i en messingring
  blank(cv) {
    for (let y = 0; y < S; y++) for (let x = 0; x < S; x++) {
      const d = Math.hypot(x - C, y - C);
      if (d > 31.5) continue;
      if (d > 27.5) { const k = 0.7 + rnd() * 0.15; cv.set(x, y, [168 * k, 128 * k, 62 * k]); }
      else { const k = 0.9 + rnd() * 0.2; cv.set(x, y, [34 * k, 30 * k, 26 * k]); }
    }
    for (const [x, y] of [[C - 29, C], [C + 29, C]]) for (let j = -1; j <= 1; j++) for (let i = -1; i <= 1; i++) cv.set(x + i, y + j, i === j ? [60, 50, 30] : [120, 110, 90]);
  }
};

for (const [name, draw] of Object.entries(dials)) {
  const cv = canvas();
  draw(cv);
  writePng(path.join(outDir, name + ".png"), S, S, cv.px);
}

// Visere: helsvart jern og rød-malt nordende (16 px, ensfarget med litt støy)
for (const [name, col] of [["needle", [24, 22, 20]], ["needlered", [170, 40, 30]]]) {
  const px = Buffer.alloc(16 * 16 * 4);
  for (let i = 0; i < 256; i++) { const k = 0.9 + rnd() * 0.2; px[i * 4] = Math.min(255, col[0] * k); px[i * 4 + 1] = Math.min(255, col[1] * k); px[i * 4 + 2] = Math.min(255, col[2] * k); px[i * 4 + 3] = 255; }
  writePng(path.join(outDir, name + ".png"), 16, 16, px);
}
// Lampeglass (16 px): av = mørkt farget glass, på = sterkt med en lys flekk i midten
for (const [name, col] of [["lampred", [235, 60, 35]], ["lampamber", [245, 170, 40]]]) {
  for (const on of [false, true]) {
    const px = Buffer.alloc(16 * 16 * 4);
    for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) {
      const d = Math.hypot(x - 7.5, y - 7.5) / 10.6, i = (y * 16 + x) * 4;
      const k = on ? 1.15 - 0.45 * d : 0.38 - 0.12 * d + (x < 6 && y < 6 && x + y > 4 && x + y < 8 ? 0.25 : 0);
      for (let c = 0; c < 3; c++) px[i + c] = Math.max(0, Math.min(255, col[c] * k + (on && d < 0.3 ? 90 : 0)));
      px[i + 3] = 255;
    }
    writePng(path.join(outDir, name + (on ? "on" : "off") + ".png"), 16, 16, px);
  }
}
console.log("Skrev urskiver til " + outDir);
