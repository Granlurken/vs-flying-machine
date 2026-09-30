// Item-shapes (= inventar-ikonene) hentet rett fra flymodellen, så komponenten i sekken ser ut som delen på flyet.
// Kjør ETTER wright_model.js: node tools/item_shapes.js
//   leser  Wright/assets/wright/shapes/entity/wright.json
//   skriver Wright/assets/wright/shapes/item/<item>.json
// Hver valgt gruppe klones i full størrelse og sentreres i (8, 8, 8) px; størrelsen i GUI/hånd/bakke styres av
// transformene i itemtypes-JSON-en (shape-elementer kan ikke skaleres).
const fs = require("fs");
const path = require("path");

const ROOT = path.join(__dirname, "..", "Wright", "assets", "wright", "shapes");
const plane = JSON.parse(fs.readFileSync(path.join(ROOT, "entity", "wright.json"), "utf8"));

// item → grupper/elementer fra flyet som utgjør det
const items = {
  propeller: ["Propeller"]
};

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
for (const [item, names] of Object.entries(items)) {
  const parts = names.map(n => {
    const e = find(plane.elements, n);
    if (!e) throw new Error(`fant ikke «${n}» i wright.json`);
    return JSON.parse(JSON.stringify(e));
  });
  // Flyets grupper har from [0,0,0], så koordinatene er i Root-rammen; flytt midten til (8, 8, 8)
  const { lo, hi } = bounds(parts);
  const off = [0, 1, 2].map(i => +(8 - (lo[i] + hi[i]) / 2).toFixed(3));
  const tex = usedTextures(parts);
  const shape = {
    textureWidth: 16, textureHeight: 16,
    textures: Object.fromEntries([...tex].map(k => [k, plane.textures[k]])),
    elements: [{ name: "Root", from: off, to: off, faces: {}, children: parts }]
  };
  fs.writeFileSync(path.join(outDir, item + ".json"), JSON.stringify(shape, null, "\t") + "\n");
  const size = [0, 1, 2].map(i => +((hi[i] - lo[i]) / 16).toFixed(2));
  console.log(`Skrev item/${item}.json — ${size.join(" × ")} blokker, teksturer: ${[...tex].join(", ")}`);
}
