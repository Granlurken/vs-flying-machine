// Item-shapes (= inventar-ikonene) hentet rett fra flymodellen, så komponenten i sekken ser ut som delen på flyet.
// Kjør ETTER flyingmachine_model.js: node tools/item_shapes.js
//   leser  FlyingMachine/assets/flyingmachine/shapes/entity/flyingmachine.json
//   skriver FlyingMachine/assets/flyingmachine/shapes/item/<item>.json
// Hver valgt gruppe klones i full størrelse og sentreres i (8, 8, 8) px; størrelsen i GUI/hånd/bakke styres av
// transformene i itemtypes-JSON-en (shape-elementer kan ikke skaleres).
const fs = require("fs");
const path = require("path");

const ROOT = path.join(__dirname, "..", "FlyingMachine", "assets", "flyingmachine", "shapes");
const plane = JSON.parse(fs.readFileSync(path.join(ROOT, "entity", "flyingmachine.json"), "utf8"));
const site = JSON.parse(fs.readFileSync(path.join(ROOT, "entity", "construction.json"), "utf8"));

// Egne deler (finnes ikke på flyet): kloss i px, UV = ett rute per 16 px som i flyingmachine_model.js
const uvLen = v => Math.max(0.5, Math.min(16, +v.toFixed(3)));
function box(name, from, to, tex, rot) {
  const [w, h, d] = [0, 1, 2].map(i => to[i] - from[i]);
  const f = (a, b) => ({ texture: "#" + tex, uv: [0, 0, uvLen(a), uvLen(b)] });
  const e = { name, from, to, faces: { north: f(w, h), east: f(d, h), south: f(w, h), west: f(d, h), up: f(w, d), down: f(w, d) } };
  if (rot) {
    e.rotationOrigin = [0, 1, 2].map(i => (from[i] + to[i]) / 2);
    [e.rotationX, e.rotationY, e.rotationZ] = rot;
  }
  return e;
}

// Veivaksel: hovedlagre langs X, to veiv-kast 180° fra hverandre, svinghjul-flens i enden
function crankshaft() {
  const els = [];
  const main = (n, x0, x1) => els.push(box(n, [x0, 3, 7], [x1, 5, 9], "iron"));
  const web = (n, x0, y0, y1) => els.push(box(n, [x0, y0, 6.5], [x0 + 1.5, y1, 9.5], "iron"));
  main("MainA", 0, 8);
  web("WebA0", 8, 0, 5.5); els.push(box("PinA", [9.5, 0.5, 7], [13, 2.5, 9], "iron")); web("WebA1", 13, 0, 5.5);
  main("MainB", 14.5, 17.5);
  web("WebB0", 17.5, 2.5, 8); els.push(box("PinB", [19, 5.5, 7], [22.5, 7.5, 9], "iron")); web("WebB1", 22.5, 2.5, 8);
  main("MainC", 24, 30);
  els.push(box("Flange", [30, 1.5, 5], [32, 6.5, 11], "iron"));
  return els;
}

// Vingebjelke: tre limte lameller (like tresorter trengs ikke — lysere midtlag) m/ harpiks-fuger og surring i endene
function wingspar() {
  const L = 32, W = 3;
  return [
    box("Lam0", [0, 0, 0], [L, 1.2, W], "planks"),
    box("Glue0", [0.2, 1.2, 0.1], [L - 0.2, 1.4, W - 0.1], "resin"),
    box("Lam1", [0, 1.4, 0], [L, 2.6, W], "planks2"),
    box("Glue1", [0.2, 2.6, 0.1], [L - 0.2, 2.8, W - 0.1], "resin"),
    box("Lam2", [0, 2.8, 0], [L, 4, W], "planks"),
    box("LashA", [3, -0.2, -0.2], [4.2, 4.2, W + 0.2], "twine"),
    box("LashB", [L - 4.2, -0.2, -0.2], [L - 3, 4.2, W + 0.2], "twine")
  ];
}

// Flyduk: brettet stabel voksbehandlet lin, bundet om med hyssing
function wingfabric() {
  const els = [];
  for (let i = 0; i < 4; i++) {
    const s = i % 2 ? 0.4 : 0;
    els.push(box("Fold" + i, [s, i * 1.1, s * 0.5], [12 + s, i * 1.1 + 1.1, 10 + s * 0.5], "fabric"));
  }
  els.push(box("Tie", [5.4, -0.15, -0.15], [6.6, 4.55, 10.35], "twine"));
  return els;
}

// Kobbertråd: en kveil — tre firkantede vindinger lagt oppå hverandre, litt forskjøvet
function copperwire() {
  const els = [], t = 0.6, L = 8;
  for (let i = 0; i < 3; i++) {
    const o = i * 0.5, y = i * t;
    els.push(box("CoilN" + i, [o, y, o], [o + L, y + t, o + t], "copper"));
    els.push(box("CoilS" + i, [o, y, o + L - t], [o + L, y + t, o + L], "copper"));
    els.push(box("CoilW" + i, [o, y, o + t], [o + t, y + t, o + L - t], "copper"));
    els.push(box("CoilE" + i, [o + L - t, y, o + t], [o + L, y + t, o + L - t], "copper"));
  }
  els.push(box("Tail", [L + 1, 0, 3], [L + 4, t, 3 + t], "copper"));
  return els;
}

// item → hvor delene kommer fra: grupper/elementer fra flyet (plane) eller byggeplassen (site), eller egne (build)
const items = {
  propeller: { src: plane, names: ["Propeller"] },
  wheel: { src: plane, names: ["WheelL"] },
  cylinder: { src: plane, names: ["Cylinder0", "Fin00", "Fin01", "Fin02", "Fin03", "Head0"] },
  engine: {
    src: plane, names: ["Crankcase", "Sump",
      "Cylinder0", "Fin00", "Fin01", "Fin02", "Fin03", "Head0", "ExhaustOut0", "ExhaustUp0",
      "Cylinder1", "Fin10", "Fin11", "Fin12", "Fin13", "Head1", "ExhaustOut1", "ExhaustUp1"]
  },
  chaindrive: { src: plane, names: ["CrankSprocketA", "CrankSprocketB", "PropSprocketA", "PropSprocketB", "ChainL", "ChainR"] },
  wing: { src: plane, names: ["S3LowerWing"] },
  trestles: { src: site, names: ["TrestleBar0", "TrestleLeg000", "TrestleLeg001", "TrestleLeg010", "TrestleLeg011"] },
  flyingmachine: { src: plane, names: ["S1Frame", "S2Gear", "S3LowerWing", "S4UpperWing", "S5Engine", "S6Drive", "S7Tail", "S8Dash", "S9Seat"] },
  battery: { src: plane, names: ["Battery"] },
  dashboard: { src: plane, names: ["Dashboard"] },
  crankshaft: { build: crankshaft },
  copperwire: { build: copperwire },
  wingspar: { build: wingspar },
  wingfabric: { build: wingfabric }
};
// Teksturer som bare egne deler bruker
const extraTextures = { resin: "game:block/resin" };

function find(els, name) {
  for (const e of els) {
    if (e.name === name) return e;
    const hit = e.children && find(e.children, name);
    if (hit) return hit;
  }
  return null;
}

// ---- samme matriseregning som preview_shape.js (VS: T(origo)·Rx·Ry·Rz·T(from−origo), barn relative til forelderens from)
const mul = (a, b) => { const o = new Array(16).fill(0); for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) for (let k = 0; k < 4; k++) o[c * 4 + r] += a[k * 4 + r] * b[c * 4 + k]; return o; };
const T = (x, y, z) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];
const Rx = a => { const c = Math.cos(a), s = Math.sin(a); return [1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, 0, 0, 1]; };
const Ry = a => { const c = Math.cos(a), s = Math.sin(a); return [c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0, 0, 0, 1]; };
const Rz = a => { const c = Math.cos(a), s = Math.sin(a); return [c, s, 0, 0, -s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]; };
const apply = (m, [x, y, z]) => [m[0] * x + m[4] * y + m[8] * z + m[12], m[1] * x + m[5] * y + m[9] * z + m[13], m[2] * x + m[6] * y + m[10] * z + m[14]];
const D = Math.PI / 180;

// Omsluttende boks (px) for elementene slik de faktisk står etter rotasjon
function bounds(els) {
  const lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
  const walk = (list, parent) => {
    for (const e of list) {
      const o = e.rotationOrigin || [0, 0, 0];
      let m = mul(parent, T(...o));
      m = mul(m, mul(Rx((e.rotationX || 0) * D), mul(Ry((e.rotationY || 0) * D), Rz((e.rotationZ || 0) * D))));
      m = mul(m, T(e.from[0] - o[0], e.from[1] - o[1], e.from[2] - o[2]));
      if (Object.keys(e.faces || {}).length) {
        const s = [0, 1, 2].map(i => e.to[i] - e.from[i]);
        for (let c = 0; c < 8; c++) {
          const p = apply(m, [c & 1 ? s[0] : 0, c & 2 ? s[1] : 0, c & 4 ? s[2] : 0]);
          p.forEach((v, i) => { lo[i] = Math.min(lo[i], v); hi[i] = Math.max(hi[i], v); });
        }
      }
      if (e.children) walk(e.children, m);
    }
  };
  walk(els, [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
  return { lo, hi };
}

const usedTextures = (els, set = new Set()) => {
  for (const e of els) {
    for (const f of Object.values(e.faces || {})) if (f.texture) set.add(f.texture.replace("#", ""));
    if (e.children) usedTextures(e.children, set);
  }
  return set;
};

const outDir = path.join(ROOT, "item");
fs.mkdirSync(outDir, { recursive: true });
const allTextures = { ...site.textures, ...plane.textures, ...extraTextures };
const only = process.argv.slice(2);
for (const [item, def] of Object.entries(items)) {
  if (only.length && !only.includes(item)) continue;
  const parts = def.build ? def.build() : def.names.map(n => {
    const e = find(def.src.elements, n);
    if (!e) throw new Error(`fant ikke «${n}» (${item})`);
    return JSON.parse(JSON.stringify(e));
  });
  // Flyets grupper har from [0,0,0], så koordinatene er i Root-rammen; flytt midten til (8, 8, 8)
  const { lo, hi } = bounds(parts);
  const off = [0, 1, 2].map(i => +(8 - (lo[i] + hi[i]) / 2).toFixed(3));
  const tex = usedTextures(parts);
  const shape = {
    textureWidth: 16, textureHeight: 16,
    textures: Object.fromEntries([...tex].map(k => [k, allTextures[k]])),
    elements: [{ name: "Root", from: off, to: off, faces: {}, children: parts }]
  };
  fs.writeFileSync(path.join(outDir, item + ".json"), JSON.stringify(shape, null, "\t") + "\n");
  const size = [0, 1, 2].map(i => +((hi[i] - lo[i]) / 16).toFixed(2));
  console.log(`Skrev item/${item}.json — ${size.join(" × ")} blokker, teksturer: ${[...tex].join(", ")}`);
}
