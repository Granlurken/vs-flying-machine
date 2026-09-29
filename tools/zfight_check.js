// Finner flater som ligger i samme plan, vender samme vei og overlapper — det som flimrer (z-fighting) i spillet.
// Kjør: node tools/zfight_check.js <shape.json>   (wright_model.js bruker findCoplanar() til å rette seg selv)
const fs = require("fs");

const mul = (a, b) => { const o = new Array(16).fill(0); for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) for (let k = 0; k < 4; k++) o[c * 4 + r] += a[k * 4 + r] * b[c * 4 + k]; return o; };
const T = (x, y, z) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];
const Rx = a => { const c = Math.cos(a), s = Math.sin(a); return [1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, 0, 0, 1]; };
const Ry = a => { const c = Math.cos(a), s = Math.sin(a); return [c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0, 0, 0, 1]; };
const Rz = a => { const c = Math.cos(a), s = Math.sin(a); return [c, s, 0, 0, -s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]; };
const ap = (m, [x, y, z]) => [m[0] * x + m[4] * y + m[8] * z + m[12], m[1] * x + m[5] * y + m[9] * z + m[13], m[2] * x + m[6] * y + m[10] * z + m[14]];
const D = Math.PI / 180;
const sub = (a, b) => a.map((v, i) => v - b[i]), dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = a => { const l = Math.hypot(...a); return a.map(v => v / l); };

function collectFaces(shape) {
const faces = [];
(function walk(els, parent, p) {
  for (const e of els) {
    const name = p ? p + "/" + e.name : e.name;
    const o = (e.rotationOrigin || [0, 0, 0]).map(v => v / 16);
    let m = mul(parent, T(...o));
    m = mul(m, mul(Rx((e.rotationX || 0) * D), mul(Ry((e.rotationY || 0) * D), Rz((e.rotationZ || 0) * D))));
    m = mul(m, T(e.from[0] / 16 - o[0], e.from[1] / 16 - o[1], e.from[2] / 16 - o[2]));
    const s = [0, 1, 2].map(i => (e.to[i] - e.from[i]) / 16);
    const P = (x, y, z) => ap(m, [x * s[0], y * s[1], z * s[2]]);
    const F = {
      north: [P(1, 1, 0), P(0, 1, 0), P(0, 0, 0), P(1, 0, 0)], south: [P(0, 1, 1), P(1, 1, 1), P(1, 0, 1), P(0, 0, 1)],
      east: [P(1, 1, 1), P(1, 1, 0), P(1, 0, 0), P(1, 0, 1)], west: [P(0, 1, 0), P(0, 1, 1), P(0, 0, 1), P(0, 0, 0)],
      up: [P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1)], down: [P(0, 0, 1), P(1, 0, 1), P(1, 0, 0), P(0, 0, 0)]
    };
    const c = P(0.5, 0.5, 0.5);
    for (const fn of Object.keys(e.faces || {})) {
      const v = F[fn];
      let n = norm(cross(sub(v[1], v[0]), sub(v[3], v[0])));
      if (dot(n, sub(v[0], c)) < 0) n = n.map(x => -x); // pek utover
      faces.push({ el: e, name: name + ":" + fn, v, n, d: dot(n, v[0]) });
    }
    if (e.children) walk(e.children, m, name);
  }
})(shape.elements, [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1], "");
return faces;
}

// 2D-overlapp i planet: separerende akse-test for to konvekse firkanter
function overlapArea(a, b, n) {
  const u = norm(sub(a.v[1], a.v[0])), w = cross(n, u);
  const to2 = v => [dot(v, u), dot(v, w)];
  const A = a.v.map(to2), B = b.v.map(to2);
  const axes = [];
  for (const poly of [A, B]) for (let i = 0; i < 4; i++) { const e = sub(poly[(i + 1) % 4].concat(0), poly[i].concat(0)); axes.push([-e[1], e[0]]); }
  let minOverlap = Infinity;
  for (const ax of axes) {
    const l = Math.hypot(...ax); if (l < 1e-9) continue;
    const pr = poly => poly.map(p => (p[0] * ax[0] + p[1] * ax[1]) / l);
    const pa = pr(A), pb = pr(B);
    const ov = Math.min(Math.max(...pa), Math.max(...pb)) - Math.max(Math.min(...pa), Math.min(...pb));
    if (ov <= 1e-4) return 0;
    minOverlap = Math.min(minOverlap, ov);
  }
  return minOverlap;
}

// Par av flater som ligger nærmere enn TOL blokker i samme plan, vender samme vei og overlapper.
// 0.006 gir margin for dybdepresisjon på avstand (resolveren i wright_model.js skyver 0.012)
const TOL = +(process.env.ZF_TOL || 0.006);
function findCoplanar(shape) {
  const faces = collectFaces(shape), hits = [];
  for (let i = 0; i < faces.length; i++) for (let j = i + 1; j < faces.length; j++) {
    const a = faces[i], b = faces[j];
    if (a.el === b.el || dot(a.n, b.n) < 0.9995 || Math.abs(a.d - b.d) > TOL) continue;
    const ov = overlapArea(a, b, a.n);
    if (ov > 0.002) hits.push({ a, b, ov });
  }
  return { hits, faceCount: faces.length };
}
module.exports = { findCoplanar, TOL };

if (require.main === module) {
  const { hits, faceCount } = findCoplanar(JSON.parse(fs.readFileSync(process.argv[2], "utf8")));
  console.log(hits.length ? hits.map(h => `${h.a.name}  ↔  ${h.b.name}  (overlapp ${h.ov.toFixed(3)})`).join("\n") : "Ingen samplan-overlapp funnet");
  console.log(`\n${hits.length} par, ${faceCount} flater sjekket`);
}
