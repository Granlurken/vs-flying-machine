// Forhåndsvisning av en VS-shape uten å starte spillet: enkel teksturert rasterisering → PNG.
// Kjør: node tools/preview_shape.js <shape.json> <ut.png> [skjul-gruppe ...]
// Matrisene følger ShapeElement.GetLocalTransformMatrix (animVersion 0): T(origo)·Rx·Ry·Rz·T(from−origo).
const fs = require("fs");
const path = require("path");
const zlib = require("zlib");

const args = process.argv.slice(2);
// --cockpit: én stor visning fra pilotens øyne (i stedet for de fire standardvinklene)
const cockpit = args.includes("--cockpit");
// --item: kameraet sikter på shapens midte med avstand etter størrelsen (item-shapes er små og sentrert i 8,8,8 px)
const itemView = args.includes("--item");
const [shapeFile, outFile, ...hidden] = args.filter(a => !a.startsWith("--"));
const shape = JSON.parse(fs.readFileSync(shapeFile, "utf8"));
const ASSETS = path.join(process.env.APPDATA, "Vintagestory", "assets");

// ---------- PNG inn/ut
function readPng(file) {
  const buf = fs.readFileSync(file);
  let pos = 8, w = 0, h = 0, ct = 0, idat = [], palette = null, trns = null;
  while (pos < buf.length) {
    const len = buf.readUInt32BE(pos), type = buf.toString("ascii", pos + 4, pos + 8), data = buf.subarray(pos + 8, pos + 8 + len);
    if (type === "IHDR") { w = data.readUInt32BE(0); h = data.readUInt32BE(4); ct = data[9]; if (data[8] !== 8) throw new Error("bitdybde " + data[8] + " i " + file); }
    else if (type === "PLTE") palette = data;
    else if (type === "tRNS") trns = data;
    else if (type === "IDAT") idat.push(data);
    pos += 12 + len;
  }
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const bpp = { 6: 4, 2: 3, 3: 1, 0: 1, 4: 2 }[ct];
  const stride = w * bpp, out = Buffer.alloc(w * h * 4), prev = Buffer.alloc(stride), line = Buffer.alloc(stride);
  for (let y = 0; y < h; y++) {
    const f = raw[y * (stride + 1)];
    for (let x = 0; x < stride; x++) {
      const v = raw[y * (stride + 1) + 1 + x], a = x >= bpp ? line[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
      let p = v;
      if (f === 1) p = v + a; else if (f === 2) p = v + b; else if (f === 3) p = v + ((a + b) >> 1);
      else if (f === 4) { const pa = Math.abs(b - c), pb = Math.abs(a - c), pc = Math.abs(a + b - 2 * c); p = v + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c); }
      line[x] = p & 255;
    }
    for (let x = 0; x < w; x++) {
      const o = (y * w + x) * 4;
      if (ct === 6) line.copy(out, o, x * 4, x * 4 + 4);
      else if (ct === 2) { out[o] = line[x * 3]; out[o + 1] = line[x * 3 + 1]; out[o + 2] = line[x * 3 + 2]; out[o + 3] = 255; }
      else if (ct === 3) { const i = line[x]; out[o] = palette[i * 3]; out[o + 1] = palette[i * 3 + 1]; out[o + 2] = palette[i * 3 + 2]; out[o + 3] = trns && i < trns.length ? trns[i] : 255; }
      else if (ct === 0) { out[o] = out[o + 1] = out[o + 2] = line[x]; out[o + 3] = 255; }
      else if (ct === 4) { out[o] = out[o + 1] = out[o + 2] = line[x * 2]; out[o + 3] = line[x * 2 + 1]; }
    }
    line.copy(prev);
  }
  return { w, h, data: out };
}
function crc32(buf) {
  let c, crc = ~0;
  for (let n = 0; n < buf.length; n++) { c = (crc ^ buf[n]) & 255; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crc = (crc >>> 8) ^ c; }
  return ~crc >>> 0;
}
function writePng(file, w, h, rgb) {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) rgb.copy(raw, y * (w * 3 + 1) + 1, y * w * 3, (y + 1) * w * 3);
  const chunk = (type, data) => {
    const b = Buffer.alloc(12 + data.length); b.writeUInt32BE(data.length, 0); b.write(type, 4, "ascii"); data.copy(b, 8);
    b.writeUInt32BE(crc32(b.subarray(4, 8 + data.length)), 8 + data.length); return b;
  };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  fs.writeFileSync(file, Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]));
}

// ---------- teksturer
const texCache = {};
function texture(key) {
  if (texCache[key]) return texCache[key];
  const loc = shape.textures[key];
  const [dom, p] = loc.includes(":") ? loc.split(":") : ["game", loc];
  const dirs = dom === "game" ? ["survival", "game"] : [dom];
  for (const d of dirs) {
    // Egne mod-teksturer ligger i FlyingMachine/assets/<domene>, ikke i spillets assets
    const root = dom === "game" ? ASSETS : path.join(__dirname, "..", "FlyingMachine", "assets");
    const f = path.join(root, d, "textures", p + ".png");
    if (fs.existsSync(f)) return (texCache[key] = readPng(f));
  }
  console.warn("Fant ikke tekstur " + loc);
  return (texCache[key] = { w: 1, h: 1, data: Buffer.from([255, 0, 255, 255]) });
}

// ---------- matriser (kolonne-major som gl-matrix/VS)
const ident = () => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
function mul(a, b) {
  const o = new Array(16).fill(0);
  for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) for (let k = 0; k < 4; k++) o[c * 4 + r] += a[k * 4 + r] * b[c * 4 + k];
  return o;
}
const T = (x, y, z) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];
const Rx = a => { const c = Math.cos(a), s = Math.sin(a); return [1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, 0, 0, 1]; };
const Ry = a => { const c = Math.cos(a), s = Math.sin(a); return [c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0, 0, 0, 1]; };
const Rz = a => { const c = Math.cos(a), s = Math.sin(a); return [c, s, 0, 0, -s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]; };
const apply = (m, [x, y, z]) => [m[0] * x + m[4] * y + m[8] * z + m[12], m[1] * x + m[5] * y + m[9] * z + m[13], m[2] * x + m[6] * y + m[10] * z + m[14]];
const D = Math.PI / 180;

// ---------- samle trekanter
const CUBE_V = [-1,-1,-1,-1,1,-1,1,1,-1,1,-1,-1, 1,-1,-1,1,1,-1,1,1,1,1,-1,1, 1,-1,1,1,1,1,-1,1,1,-1,-1,1, -1,-1,1,-1,1,1,-1,1,-1,-1,-1,-1, 1,1,1,1,1,-1,-1,1,-1,-1,1,1, -1,-1,1,-1,-1,-1,1,-1,-1,1,-1,1];
const CUBE_UV = [1,0,1,1,0,1,0,0, 1,0,1,1,0,1,0,0, 1,0,1,1,0,1,0,0, 1,0,1,1,0,1,0,0, 1,0,1,1,0,1,0,0, 1,0,1,1,0,1,0,0];
const quads = [];
function walk(els, parent, pathName) {
  for (const e of els) {
    const full = pathName ? pathName + "/" + e.name : e.name;
    if (hidden.some(h => full === h || full.startsWith(h + "/"))) continue;
    const o = (e.rotationOrigin || [0, 0, 0]).map(v => v / 16);
    let m = mul(parent, T(...o));
    m = mul(m, mul(Rx((e.rotationX || 0) * D), mul(Ry((e.rotationY || 0) * D), Rz((e.rotationZ || 0) * D))));
    m = mul(m, T(e.from[0] / 16 - o[0], e.from[1] / 16 - o[1], e.from[2] / 16 - o[2]));
    const [sx, sy, sz] = [0, 1, 2].map(i => (e.to[i] - e.from[i]) / 16);
    const P = (x, y, z) => apply(m, [x * sx, y * sy, z * sz]);
    // Hjørner og UV per flate rett fra VS (CubeMeshUtil.CubeVertices/CubeUvCoords, ModelCubeUtilExt.AddFace):
    // U = uv[0] + a·(uv[2]−uv[0]), V = uv[3] + b·(uv[1]−uv[3])
    for (const [fi, fn] of ["north", "east", "south", "west", "up", "down"].entries()) {
      const face = (e.faces || {})[fn];
      if (!face || !face.texture) continue;
      const uv = face.uv || [0, 0, 16, 16];
      const v = [], uvs = [];
      for (let j = 0; j < 4; j++) {
        const c = CUBE_V.slice(fi * 12 + j * 3, fi * 12 + j * 3 + 3).map(k => (k + 1) / 2);
        v.push(P(...c));
        const [ua, vb] = CUBE_UV.slice(fi * 8 + j * 2, fi * 8 + j * 2 + 2);
        uvs.push([uv[0] + ua * (uv[2] - uv[0]), uv[3] + vb * (uv[1] - uv[3])]);
      }
      quads.push({ v, tex: face.texture.replace("#", ""), uvs });
    }
    if (e.children) walk(e.children, m, full);
  }
}
walk(shape.elements, ident(), "");

// ---------- rasterisering med z-buffer, 2× supersampling
const views = [
  { name: "3/4 foran", yaw: -35, pitch: 22, dist: 13 },
  { name: "3/4 bak", yaw: 145, pitch: 18, dist: 13 },
  { name: "side", yaw: 90, pitch: 3, dist: 14 },
  { name: "ovenfra", yaw: 90, pitch: 80, dist: 15 }
];
// Pilotens øyne: design (0.4, 2.2, 0) → verden (0.5−x, y, 0.5−z) (barn er relative til Root sin from, og Root er snudd 180°)
if (cockpit) views.splice(0, views.length, { name: "cockpit", eye: [0.1, 2.2, 0.5], look: [-0.9, 1.55, 0.5] });
const VW = cockpit ? 1280 : 640, VH = cockpit ? 800 : 400, SS = 2, W = VW * (cockpit ? 1 : 2) * SS, H = VH * (cockpit ? 1 : 2) * SS;
const color = new Float32Array(W * H * 3), zbuf = new Float32Array(W * H).fill(Infinity);
for (let i = 0; i < W * H; i++) { const y = Math.floor(i / W) % (VH * SS); const t = y / (VH * SS); color[i * 3] = 150 + 60 * t; color[i * 3 + 1] = 170 + 50 * t; color[i * 3 + 2] = 190 + 30 * t; }
const light = (() => { const l = [0.4, 0.8, 0.3]; const n = Math.hypot(...l); return l.map(v => v / n); })();

// Kameraet ser mot flyets midte. Shapen er snudd 180° i Root, så «foran» i verden er −X.
let target = [0.5, 1.1, 0.5];
if (itemView) {
  const lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
  for (const q of quads) for (const p of q.v) p.forEach((v, i) => { lo[i] = Math.min(lo[i], v); hi[i] = Math.max(hi[i], v); });
  target = lo.map((v, i) => (v + hi[i]) / 2);
  const k = Math.hypot(...hi.map((v, i) => v - lo[i])) / 2.4;
  views.forEach(v => { v.dist *= k / 2; });
}
views.forEach((view, vi) => {
  const ox = (vi % 2) * VW * SS, oy = Math.floor(vi / 2) * VH * SS;
  const yaw = (view.yaw || 0) * D, pitch = (view.pitch || 0) * D;
  const tgt = view.look || target;
  const eye = view.eye || [target[0] - Math.cos(pitch) * Math.cos(yaw) * view.dist, target[1] + Math.sin(pitch) * view.dist, target[2] - Math.cos(pitch) * Math.sin(yaw) * view.dist];
  const fwd = tgt.map((v, i) => v - eye[i]); const fl = Math.hypot(...fwd); fwd.forEach((v, i) => (fwd[i] = v / fl));
  let right = [fwd[2], 0, -fwd[0]]; const rl = Math.hypot(...right) || 1; right = right.map(v => v / rl);
  right = right.map(v => -v);
  const up = [right[1] * fwd[2] - right[2] * fwd[1], right[2] * fwd[0] - right[0] * fwd[2], right[0] * fwd[1] - right[1] * fwd[0]];
  const f = VH * SS * (cockpit ? 0.9 : 1.6);
  const project = p => {
    const d = [p[0] - eye[0], p[1] - eye[1], p[2] - eye[2]];
    const z = d[0] * fwd[0] + d[1] * fwd[1] + d[2] * fwd[2];
    return [ox + VW * SS / 2 + (d[0] * right[0] + d[1] * right[1] + d[2] * right[2]) / z * f, oy + VH * SS / 2 - (d[0] * up[0] + d[1] * up[1] + d[2] * up[2]) / z * f, z];
  };
  // Bakke
  const g = [[-6, 0, -6], [6, 0, -6], [6, 0, 6], [-6, 0, 6]];
  for (const q of quads.concat([{ v: g, ground: true }])) {
    const n = (() => { const a = q.v[1].map((v, i) => v - q.v[0][i]), b = q.v[3].map((v, i) => v - q.v[0][i]); const c = [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]; const l = Math.hypot(...c) || 1; return c.map(v => v / l); })();
    const shade = 0.55 + 0.45 * Math.abs(n[0] * light[0] + n[1] * light[1] + n[2] * light[2]);
    const tex = q.ground ? null : texture(q.tex);
    const uvs = q.ground ? [[0, 0], [16, 0], [16, 16], [0, 16]] : q.uvs;
    const pts = q.v.map(project);
    if (pts.some(p => p[2] <= 0.1)) continue;
    for (const tri of [[0, 1, 2], [0, 2, 3]]) {
      const [A, B, C] = tri.map(i => pts[i]), [ua, ub, uc] = tri.map(i => uvs[i]);
      const minX = Math.max(ox, Math.floor(Math.min(A[0], B[0], C[0]))), maxX = Math.min(ox + VW * SS - 1, Math.ceil(Math.max(A[0], B[0], C[0])));
      const minY = Math.max(oy, Math.floor(Math.min(A[1], B[1], C[1]))), maxY = Math.min(oy + VH * SS - 1, Math.ceil(Math.max(A[1], B[1], C[1])));
      const area = (B[0] - A[0]) * (C[1] - A[1]) - (B[1] - A[1]) * (C[0] - A[0]);
      if (Math.abs(area) < 1e-6) continue;
      for (let y = minY; y <= maxY; y++) for (let x = minX; x <= maxX; x++) {
        const w0 = ((B[0] - x) * (C[1] - y) - (B[1] - y) * (C[0] - x)) / area;
        const w1 = ((C[0] - x) * (A[1] - y) - (C[1] - y) * (A[0] - x)) / area;
        const w2 = 1 - w0 - w1;
        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
        const z = w0 * A[2] + w1 * B[2] + w2 * C[2] + (q.ground ? 0.02 : 0);
        const idx = y * W + x;
        if (z >= zbuf[idx]) continue;
        let r, gg, b;
        if (q.ground) { r = 110; gg = 128; b = 80; }
        else {
          const u = (w0 * ua[0] + w1 * ub[0] + w2 * uc[0]) / 16, v = (w0 * ua[1] + w1 * ub[1] + w2 * uc[1]) / 16;
          const tx = Math.min(tex.w - 1, Math.max(0, Math.floor(u * tex.w))), ty = Math.min(tex.h - 1, Math.max(0, Math.floor(v * tex.h)));
          const o = (ty * tex.w + tx) * 4;
          if (tex.data[o + 3] < 128) continue;
          r = tex.data[o]; gg = tex.data[o + 1]; b = tex.data[o + 2];
        }
        zbuf[idx] = z;
        color[idx * 3] = r * shade; color[idx * 3 + 1] = gg * shade; color[idx * 3 + 2] = b * shade;
      }
    }
  }
});
const OW = W / SS, OH = H / SS, out = Buffer.alloc(OW * OH * 3);
for (let y = 0; y < OH; y++) for (let x = 0; x < OW; x++) for (let c = 0; c < 3; c++) {
  let s = 0; for (let dy = 0; dy < SS; dy++) for (let dx = 0; dx < SS; dx++) s += color[((y * SS + dy) * W + x * SS + dx) * 3 + c];
  out[(y * OW + x) * 3 + c] = Math.min(255, Math.round(s / (SS * SS)));
}
writePng(outFile, OW, OH, out);
console.log(`Skrev ${outFile} (${quads.length} flater)`);
