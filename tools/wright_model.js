// Genererer den ekte Wright-modellen: grov biplan i tre, lin og jern, Vintage Story-stil.
// Kjør: node tools/wright_model.js
//   → Wright/assets/wright/shapes/entity/wright.json         (flyet, S1–S8)
//   → Wright/assets/wright/shapes/entity/construction.json   (byggeplassen, S0Site–S8Seat)
// Begge bygges fra de samme delene, så byggeplassen og flyet aldri kan gli fra hverandre.
//
// Designramme: mål i blokker, (0,0,0) = bakkenivå midt under flyet, nesa mot +X, opp = +Y, høyre = +Z.
// Vanilla-båtene har nesa mot −X (roten deres er snudd 180°), så Root snus 180° her også.
// Da gjelder de testede fortegnene i entity-JSON (visualPitchSign −1) og seter vender framover med rotasjon 0.
const fs = require("fs");
const path = require("path");

const PX = 16;
const r3 = v => +v.toFixed(3);
const px = v => r3(v * PX);
const deg = rad => r3(rad * 180 / Math.PI);

// UV: ett tekstur-rute (16 px) per blokk, kappet på 16 så lange flater strekkes i stedet for å gjentas
const uvLen = v => Math.max(0.5, Math.min(16, r3(v * PX)));
function faces(tex, w, h, d, skip = []) {
  const f = (a, b) => ({ texture: "#" + tex, uv: [0, 0, uvLen(a), uvLen(b)] });
  const all = { north: f(w, h), east: f(d, h), south: f(w, h), west: f(d, h), up: f(w, d), down: f(w, d) };
  for (const s of skip) delete all[s];
  return all;
}

// Aksejustert kloss fra hjørne til hjørne
function box(name, a, b, tex, opts = {}) {
  const lo = [0, 1, 2].map(i => Math.min(a[i], b[i])), hi = [0, 1, 2].map(i => Math.max(a[i], b[i]));
  const [w, h, d] = [0, 1, 2].map(i => hi[i] - lo[i]);
  const e = { name, from: lo.map(px), to: hi.map(px), faces: tex ? faces(tex, w, h, d, opts.skip) : {} };
  if (opts.rot) {
    e.rotationOrigin = (opts.origin || lo.map((v, i) => (lo[i] + hi[i]) / 2)).map(px);
    if (opts.rot[0]) e.rotationX = opts.rot[0];
    if (opts.rot[1]) e.rotationY = opts.rot[1];
    if (opts.rot[2]) e.rotationZ = opts.rot[2];
  }
  return e;
}

// Bjelke fra p1 til p2 med tverrsnitt t (lokal Y) × tw (lokal Z).
// VS roterer X·Y·Z (Z først): Z løfter bjelken, Y svinger den. rotX (valgfri) dreier om design-X etterpå.
function beam(name, p1, p2, t, tex, tw = t, rotX = 0) {
  const d = [p2[0] - p1[0], p2[1] - p1[1], p2[2] - p1[2]];
  const L = Math.hypot(...d);
  const e = {
    name,
    from: [px(p1[0]), px(p1[1] - t / 2), px(p1[2] - tw / 2)],
    to: [px(p1[0] + L), px(p1[1] + t / 2), px(p1[2] + tw / 2)],
    rotationOrigin: p1.map(px),
    faces: faces(tex, L, t, tw)
  };
  const ry = deg(Math.atan2(-d[2], d[0])), rz = deg(Math.asin(d[1] / L));
  if (rotX) e.rotationX = rotX;
  if (ry) e.rotationY = ry;
  if (rz) e.rotationZ = rz;
  return e;
}

// Slitasje: fast frø, så modellen blir lik hver gang skriptet kjøres
let seed = 1903;
const rnd = () => { seed = (seed + 0x6D2B79F5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
const rr = (a, b) => a + (b - a) * rnd();
const pick = arr => arr[Math.floor(rnd() * arr.length)];

const group = (name, children, extra = {}) => ({ name, from: [0, 0, 0], to: [0, 0, 0], faces: {}, ...extra, children });

// Usynlig kloss som bærer et festepunkt. Klikkboksen = klossen, lagt ut fra festepunktet.
function apBox(name, code, center, size, apAt = "corner") {
  const a = center.map((v, i) => v - size[i] / 2), b = center.map((v, i) => v + size[i] / 2);
  const e = box(name, a, b, null);
  // "seat": rytteren sitter midt på setet (bunnen av klossen)
  const pos = apAt === "seat" ? [size[0] / 2, 0, size[2] / 2] : [0, 0, 0];
  e.attachmentpoints = [{
    code,
    posX: String(px(pos[0])), posY: String(px(pos[1])), posZ: String(px(pos[2])),
    rotationX: "0", rotationY: "0", rotationZ: "0"
  }];
  return e;
}

// Hjul i XY-planet: 8 felger, 4 eiker, jernnav
function wheel(prefix, cx, cy, cz, r, width) {
  const out = [];
  const seg = 2 * r * Math.tan(Math.PI / 8) + 0.02;
  for (let k = 0; k < 8; k++) {
    const a = k * Math.PI / 4, tx = -Math.sin(a), ty = Math.cos(a);
    const mx = cx + r * Math.cos(a), my = cy + r * Math.sin(a);
    out.push(beam(`${prefix}Rim${k}`, [mx - tx * seg / 2, my - ty * seg / 2, cz], [mx + tx * seg / 2, my + ty * seg / 2, cz], 0.09, "planks", width));
  }
  for (let k = 0; k < 4; k++) {
    const a = k * Math.PI / 4 + Math.PI / 8, dx = Math.cos(a) * r, dy = Math.sin(a) * r;
    out.push(beam(`${prefix}Spoke${k}`, [cx - dx, cy - dy, cz], [cx + dx, cy + dy, cz], 0.05, "wood", 0.05));
  }
  out.push(box(`${prefix}Hub`, [cx - 0.08, cy - 0.08, cz - width / 2 - 0.03], [cx + 0.08, cy + 0.08, cz + width / 2 + 0.03], "iron"));
  return out;
}

// Vinge: to bjelker, ribber, flyduk-felt mellom ribbene, avrundet tipp
function wing(prefix, y, x0, x1, span, ribStep) {
  const out = [];
  const chord = x1 - x0;
  out.push(beam(`${prefix}FrontSpar`, [x1 - 0.12, y, -span], [x1 - 0.12, y, span], 0.1, "wood", 0.1));
  out.push(beam(`${prefix}RearSpar`, [x0 + 0.3, y, -span], [x0 + 0.3, y, span], 0.08, "wood", 0.08));
  out.push(box(`${prefix}LeadingEdge`, [x1 - 0.05, y - 0.04, -span], [x1, y + 0.04, span], "wood"));
  const n = Math.round(2 * span / ribStep);
  const step = 2 * span / n;
  for (let i = 0; i <= n; i++) {
    const z = -span + i * step;
    const tip = i === 0 || i === n;
    const xa = tip ? x0 + 0.15 : x0, xb = tip ? x1 - 0.1 : x1;
    out.push(box(`${prefix}Rib${i}`, [xa, y - 0.05, z - 0.025], [xb, y + 0.05, z + 0.025], "wood"));
    if (i < n) {
      out.push(box(`${prefix}Panel${i}`, [x0, y - 0.03, z + 0.025], [x1, y + 0.03, z + step - 0.025], "fabric"));
      const za = z + 0.025, zb = z + step - 0.025;
      // Lapp sydd over et hull i duken
      if (rnd() < 0.3) {
        const lw = rr(0.22, 0.42), ld = rr(0.18, Math.min(0.4, zb - za - 0.06));
        const lx = rr(x0 + 0.08, x1 - 0.08 - lw), lz = rr(za + 0.03, zb - 0.03 - ld);
        out.push(box(`${prefix}Patch${i}`, [lx, y + 0.03, lz], [lx + lw, y + 0.037, lz + ld], pick(["patch", "patch2", "patch3"]),
          { rot: [0, r3(rr(-8, 8)), 0] }));
      }
      // Krysstråder: hyssing strammet på skrå over feltet (duken holdt ikke formen alene)
      if (rnd() < 0.35) {
        const yy = y + 0.035;
        out.push(beam(`${prefix}ThreadA${i}`, [x0 + 0.06, yy, za + 0.03], [x1 - 0.06, yy, zb - 0.03], 0.012, "twine", 0.012));
        out.push(beam(`${prefix}ThreadB${i}`, [x0 + 0.06, yy, zb - 0.03], [x1 - 0.06, yy, za + 0.03], 0.012, "twine", 0.012));
      }
    }
  }
  // Tippbuen: to korte trinn så enden ikke er helt firkantet
  [-1, 1].forEach((s, j) => {
    const z = s * span;
    out.push(box(`${prefix}Tip${j}`, [x0 + 0.15, y - 0.025, z], [x1 - 0.1, y + 0.025, z + s * 0.12], "fabric"));
    out.push(box(`${prefix}TipBow${j}`, [x0 + 0.35, y - 0.03, z + s * 0.12], [x1 - 0.25, y + 0.03, z + s * 0.18], "wood"));
  });
  void chord;
  return out;
}

// ---------------------------------------------------------------- mål
const NOSE = 2.2;          // brannvegg / nesa på skroget
const COCKPIT_BACK = 0.2;  // der skroget begynner å smalne
const TAIL = -3.95;        // halestolpen
const W = 0.45, WT = 0.12; // halv bredde foran / ved halen
const BOT = 0.75, BOT_T = 1.0, TOP = 1.45, TOP_T = 1.3;
const LOWER_Y = 0.8, UPPER_Y = 2.35;
const lerp = (a, b, t) => a + (b - a) * t;
const tailT = x => Math.max(0, Math.min(1, (COCKPIT_BACK - x) / (COCKPIT_BACK - TAIL)));
const halfW = x => lerp(W, WT, tailT(x));
const botY = x => lerp(BOT, BOT_T, tailT(x));
const topY = x => lerp(TOP, TOP_T, tailT(x));

// ---------------------------------------------------------------- S0: bukker + taugjerde
const site = [];
{
  const R = 4.45, postH = 1.2, ropeY = 1.0;
  [[-1, -1], [1, -1], [1, 1], [-1, 1]].forEach(([sx, sz], i) => {
    site.push(box("Post" + i, [sx * R - 0.08, 0, sz * R - 0.08], [sx * R + 0.08, postH, sz * R + 0.08], "aged"));
    site.push(box("PostCap" + i, [sx * R - 0.1, postH - 0.1, sz * R - 0.1], [sx * R + 0.1, postH - 0.04, sz * R + 0.1], "rope"));
  });
  // Tauet henger litt mellom stolpene: to skrå biter per side
  const sag = 0.12;
  const corners = [[-R, -R], [R, -R], [R, R], [-R, R]];
  corners.forEach((c, i) => {
    const n = corners[(i + 1) % 4];
    const mid = [(c[0] + n[0]) / 2, ropeY - sag, (c[1] + n[1]) / 2];
    site.push(beam(`Rope${i}a`, [c[0], ropeY, c[1]], mid, 0.05, "rope"));
    site.push(beam(`Rope${i}b`, mid, [n[0], ropeY, n[1]], 0.05, "rope"));
  });
  // To sagkrakker under skroget (bukkene)
  [[-2.6, botY(-2.6)], [1.6, BOT]].forEach(([x, yTop], i) => {
    const barY = yTop - 0.07;
    site.push(box(`TrestleBar${i}`, [x - 0.07, barY - 0.12, -0.8], [x + 0.07, barY, 0.8], "aged"));
    [-1, 1].forEach((s, j) => {
      [-1, 1].forEach((f, k) => {
        site.push(beam(`TrestleLeg${i}${j}${k}`, [x, barY - 0.1, s * 0.62], [x + f * 0.3, 0, s * 0.74], 0.07, "aged"));
      });
    });
  });
  // Planker og en kveil tau lagt fram på plassen
  site.push(box("PlankPile0", [-3.4, 0, 2.4], [-1.4, 0.06, 2.75], "planks"));
  site.push(box("PlankPile1", [-3.3, 0.06, 2.45], [-1.5, 0.12, 2.8], "planks", { rot: [0, 4, 0] }));
  site.push(box("RopeCoil0", [2.6, 0, -3.3], [3.1, 0.1, -2.8], "rope"));
  site.push(box("RopeCoil1", [2.7, 0.1, -3.2], [3.0, 0.16, -2.9], "rope"));
}

// ---------------------------------------------------------------- S1: skrog (fagverk av tre)
const frame = [];
{
  const stations = [NOSE, 1.2, COCKPIT_BACK, -1.0, -2.1, -3.1, TAIL];
  const th = 0.1;
  for (const s of [-1, 1]) {
    const side = s < 0 ? "L" : "R";
    // Lengdebjelker: rett stykke foran, skrått smalnende bak
    frame.push(beam(`LongBot${side}0`, [NOSE, BOT, s * W], [COCKPIT_BACK, BOT, s * W], th, "wood"));
    frame.push(beam(`LongBot${side}1`, [COCKPIT_BACK, BOT, s * W], [TAIL, BOT_T, s * WT], th, "wood"));
    frame.push(beam(`LongTop${side}0`, [NOSE, TOP, s * W], [COCKPIT_BACK, TOP, s * W], th, "wood"));
    frame.push(beam(`LongTop${side}1`, [COCKPIT_BACK, TOP, s * W], [TAIL, TOP_T, s * WT], th, "wood"));
    stations.forEach((x, i) => {
      frame.push(beam(`Upright${side}${i}`, [x, botY(x), s * halfW(x)], [x, topY(x), s * halfW(x)], 0.08, "wood"));
    });
    // Kryssavstivning med tau i de bakre feltene
    for (let i = 2; i < stations.length - 1; i++) {
      const xa = stations[i], xb = stations[i + 1];
      const za = s * (halfW(xa) + 0.01), zb = s * (halfW(xb) + 0.01);
      frame.push(beam(`Brace${side}${i}a`, [xa, botY(xa) + 0.05, za], [xb, topY(xb) - 0.05, zb], 0.025, "rope"));
      frame.push(beam(`Brace${side}${i}b`, [xa, topY(xa) - 0.05, za], [xb, botY(xb) + 0.05, zb], 0.025, "rope"));
    }
  }
  stations.forEach((x, i) => {
    const hw = halfW(x);
    frame.push(box(`CrossBot${i}`, [x - 0.04, botY(x) - 0.04, -hw], [x + 0.04, botY(x) + 0.04, hw], "wood"));
    frame.push(box(`CrossTop${i}`, [x - 0.04, topY(x) - 0.04, -hw], [x + 0.04, topY(x) + 0.04, hw], "wood"));
  });
  // Brannvegg foran, plankekledning rundt motorrommet, lave sidebord ved setene
  frame.push(box("Firewall", [NOSE - 0.05, BOT, -W], [NOSE + 0.02, TOP, W], "planks"));
  for (const s of [-1, 1]) {
    const side = s < 0 ? "L" : "R";
    frame.push(box(`CowlSide${side}`, [1.15, BOT - 0.02, s * (W + 0.02)], [NOSE, TOP + 0.02, s * (W + 0.05)], "planks"));
    frame.push(box(`CockpitSide${side}`, [-1.55, BOT, s * (W + 0.02)], [1.15, BOT + 0.38, s * (W + 0.05)], "planks"));
    frame.push(box(`CockpitRail${side}`, [-1.55, BOT + 0.38, s * (W - 0.02)], [1.15, BOT + 0.44, s * (W + 0.07)], "wood"));
  }
  frame.push(box("Deck", [1.15, TOP, -W - 0.05], [NOSE, TOP + 0.05, W + 0.05], "planks"));
  frame.push(box("Floor", [-1.55, BOT + 0.02, -W + 0.04], [1.15, BOT + 0.07, W - 0.04], "planks"));
  // Lastehylle bak piloten
  frame.push(box("CargoShelf", [-2.35, botY(-2.35) + 0.05, -halfW(-2.35) + 0.03], [-1.55, botY(-1.55) + 0.1, halfW(-1.55) - 0.03], "planks"));
  frame.push(box("CargoRailBack", [-2.4, botY(-2.4) + 0.05, -halfW(-2.4)], [-2.33, botY(-2.4) + 0.4, halfW(-2.4)], "wood"));
}

// ---------------------------------------------------------------- S2: understell
const gear = [];
{
  const wx = 1.3, wy = 0.4, wz = 0.98, r = 0.38;
  // Hvert hjul er en egen gruppe med origo i navet — animasjonen «wheelspin» dreier dem om Z
  gear.push(group("WheelL", wheel("WheelL", wx, wy, -wz, r, 0.13), { rotationOrigin: [wx, wy, -wz].map(px) }));
  gear.push(group("WheelR", wheel("WheelR", wx, wy, wz, r, 0.13), { rotationOrigin: [wx, wy, wz].map(px) }));
  gear.push(box("Axle", [wx - 0.035, wy - 0.035, -wz - 0.1], [wx + 0.035, wy + 0.035, wz + 0.1], "iron"));
  for (const s of [-1, 1]) {
    const side = s < 0 ? "L" : "R";
    gear.push(beam(`VStrutFront${side}`, [wx + 0.02, wy, s * (wz - 0.12)], [NOSE - 0.1, BOT, s * W], 0.08, "wood"));
    gear.push(beam(`VStrutBack${side}`, [wx - 0.02, wy, s * (wz - 0.12)], [0.55, BOT, s * W], 0.08, "wood"));
  }
  gear.push(beam("AxleBraceA", [wx, wy + 0.02, -wz + 0.15], [wx, BOT - 0.03, W - 0.05], 0.025, "rope"));
  gear.push(beam("AxleBraceB", [wx, wy + 0.02, wz - 0.15], [wx, BOT - 0.03, -W + 0.05], 0.025, "rope"));
  // Halesporen: en krum stokk under halen, avstivet med et stag
  gear.push(beam("TailSkid", [TAIL + 0.35, botY(TAIL + 0.35), 0], [TAIL - 0.12, 0.02, 0], 0.08, "wood", 0.08));
  gear.push(beam("TailSkidStay", [TAIL + 1.1, botY(TAIL + 1.1), 0], [TAIL + 0.02, 0.35, 0], 0.05, "wood", 0.05));
  gear.push(box("TailSkidShoe", [TAIL - 0.2, 0, -0.05], [TAIL + 0.05, 0.04, 0.05], "iron"));
}

// ---------------------------------------------------------------- S3: nedre vinge
const lowerWing = wing("Lower", LOWER_Y, -0.3, 1.0, 4.15, 0.65);

// ---------------------------------------------------------------- S4: stag + øvre vinge
const upperWing = wing("Upper", UPPER_Y, -0.15, 1.15, 4.3, 0.65);
{
  // Mellomvinge-stag (to par per side) og kabanstag ned til skroget
  const bays = [1.55, 3.35];
  const front = 0.85, rear = 0.1;
  for (const s of [-1, 1]) {
    const side = s < 0 ? "L" : "R";
    bays.forEach((z, i) => {
      [["F", front + 0.05], ["R", rear]].forEach(([k, x]) => {
        upperWing.push(box(`Strut${side}${i}${k}`, [x - 0.045, LOWER_Y, s * z - 0.045], [x + 0.045, UPPER_Y, s * z + 0.045], pick(["wood", "wood", "aged"]),
          { rot: [r3(rr(-1.5, 1.5)), 0, r3(rr(-2, 2))] }));
        // Tausurring der staget møter vingene
        [LOWER_Y + 0.07, UPPER_Y - 0.12].forEach((yy, j) =>
          upperWing.push(box(`Lash${side}${i}${k}${j}`, [x - 0.06, yy, s * z - 0.06], [x + 0.06, yy + 0.07, s * z + 0.06], "rope")));
      });
    });
    upperWing.push(beam(`Cabane${side}F`, [0.95, TOP, s * W], [0.9, UPPER_Y - 0.05, s * 0.5], 0.07, "wood"));
    upperWing.push(beam(`Cabane${side}R`, [0.2, TOP, s * W], [0.15, UPPER_Y - 0.05, s * 0.5], 0.07, "wood"));
    // Barduner (tau) i kryss i fremre stagplan, indre og ytre felt
    const zs = [W, bays[0], bays[1]];
    for (let i = 0; i < 2; i++) {
      const za = s * zs[i], zb = s * zs[i + 1];
      const ya = i === 0 ? BOT + 0.05 : LOWER_Y + 0.05;
      upperWing.push(beam(`Wire${side}${i}a`, [front, ya, za], [front, UPPER_Y - 0.05, zb], 0.025, "rope"));
      upperWing.push(beam(`Wire${side}${i}b`, [front, UPPER_Y - 0.05, za], [front, LOWER_Y + 0.05, zb], 0.025, "rope"));
    }
  }
}

// ---------------------------------------------------------------- S5: motor + drivstofftank
const engine = [];
const FUEL_CAP = [1.62, TOP + 0.47, 0];
{
  const ex0 = NOSE + 0.02, ex1 = NOSE + 0.62;
  engine.push(box("Bearer0", [NOSE - 0.1, 0.9, -0.28], [ex1 + 0.08, 0.98, -0.2], "wood"));
  engine.push(box("Bearer1", [NOSE - 0.1, 0.9, 0.2], [ex1 + 0.08, 0.98, 0.28], "wood"));
  engine.push(box("Crankcase", [ex0, 0.98, -0.26], [ex1, 1.3, 0.26], "iron"));
  engine.push(box("Sump", [ex0 + 0.08, 0.86, -0.18], [ex1 - 0.08, 0.98, 0.18], "iron"));
  // To oppstående sylindre med kjøleribber (akkurat som oppskriften: 2 sylindre + veivaksel)
  [ex0 + 0.16, ex1 - 0.16].forEach((cx, i) => {
    engine.push(box(`Cylinder${i}`, [cx - 0.11, 1.3, -0.11], [cx + 0.11, 1.78, 0.11], "iron"));
    for (let f = 0; f < 4; f++) {
      const y = 1.36 + f * 0.1;
      engine.push(box(`Fin${i}${f}`, [cx - 0.15, y, -0.15], [cx + 0.15, y + 0.03, 0.15], "iron"));
    }
    engine.push(box(`Head${i}`, [cx - 0.12, 1.78, -0.12], [cx + 0.12, 1.84, 0.12], "iron"));
    // Eksosrør: kort stuss ut til høyre, så opp
    engine.push(box(`ExhaustOut${i}`, [cx - 0.03, 1.6, 0.11], [cx + 0.03, 1.66, 0.34], "iron"));
    engine.push(box(`ExhaustUp${i}`, [cx - 0.035, 1.6, 0.3], [cx + 0.035, 1.95, 0.37], "iron"));
  });
  // Tank i kobber på dekket foran piloten, festet med jernbånd
  const tx0 = 1.2, tx1 = 2.05, ty0 = TOP + 0.05, ty1 = TOP + 0.4;
  engine.push(box("Tank", [tx0, ty0 + 0.05, -0.25], [tx1, ty1 - 0.05, 0.25], "copper"));
  engine.push(box("TankRound", [tx0 + 0.02, ty0, -0.18], [tx1 - 0.02, ty1, 0.18], "copper"));
  [tx0 + 0.15, tx1 - 0.2].forEach((x, i) =>
    engine.push(box(`TankStrap${i}`, [x, ty0 - 0.01, -0.27], [x + 0.05, ty1 + 0.01, 0.27], "iron")));
  engine.push(box("FuelCapNeck", [FUEL_CAP[0] - 0.05, ty1, -0.05], [FUEL_CAP[0] + 0.05, FUEL_CAP[1] - 0.03, 0.05], "copper"));
  engine.push(box("FuelCap", [FUEL_CAP[0] - 0.08, FUEL_CAP[1] - 0.03, -0.08], [FUEL_CAP[0] + 0.08, FUEL_CAP[1] + 0.02, 0.08], "iron"));
  engine.push(beam("FuelLine", [tx1 - 0.05, ty0 + 0.05, -0.2], [ex0 + 0.1, 1.2, -0.27], 0.03, "copper"));
  // Påfyllingstrakt på venstre side av motoren (lett å nå fra bakken), rør inn i veivhuset
  const fx = ex0 + 0.3, fz = -0.46;
  engine.push(box("FillerPipe", [fx - 0.04, 1.08, -0.4], [fx + 0.04, 1.16, -0.26], "copper"));
  engine.push(box("FillerNeck", [fx - 0.05, 1.08, fz - 0.05], [fx + 0.05, 1.3, fz + 0.05], "copper"));
  engine.push(box("FillerFunnel", [fx - 0.1, 1.3, fz - 0.1], [fx + 0.1, 1.4, fz + 0.1], "copper"));
  engine.push(box("FillerCap", [fx - 0.08, 1.4, fz - 0.08], [fx + 0.08, 1.44, fz + 0.08], "iron"));
  // Klikkboks for påfylling (FuelAP): kobbertanken + hele motoren + trakta — alt foran cockpiten er «drivstoff»
  engine.push(apBox("FuelBox", "FuelAP", [(tx0 - 0.05 + ex1 + 0.1) / 2, 1.4, -0.05], [ex1 + 0.1 - (tx0 - 0.05), 1.1, 1.0]));
}

// ---------------------------------------------------------------- S6: drivkjede + propell
const drive = [];
{
  const sx = NOSE + 0.7, crankY = 1.1, shaftY = 1.5;
  const sprocket = (name, y, r) => {
    drive.push(box(name + "A", [sx - 0.03, y - r, -r], [sx + 0.03, y + r, r], "iron"));
    drive.push(box(name + "B", [sx - 0.03, y - r, -r], [sx + 0.03, y + r, r], "iron", { rot: [45, 0, 0], origin: [sx, y, 0] }));
  };
  sprocket("CrankSprocket", crankY, 0.1);
  sprocket("PropSprocket", shaftY, 0.15);
  drive.push(beam("ChainL", [sx + 0.045, crankY, -0.13], [sx + 0.045, shaftY, -0.2], 0.035, "chain"));
  drive.push(beam("ChainR", [sx + 0.045, crankY, 0.13], [sx + 0.045, shaftY, 0.2], 0.035, "chain"));
  drive.push(box("ShaftBearing", [NOSE + 0.3, shaftY - 0.07, -0.07], [sx - 0.03, shaftY + 0.07, 0.07], "iron"));
  drive.push(beam("BearingPostA", [NOSE + 0.45, 1.3, -0.05], [NOSE + 0.45, shaftY - 0.06, -0.05], 0.05, "iron"));
  drive.push(box("Shaft", [sx, shaftY - 0.035, -0.035], [sx + 0.3, shaftY + 0.035, 0.035], "iron"));
  // Propell: to tre-blader som smalner mot tuppen, dreid litt ut av loddrett
  const hubX = sx + 0.22;
  const propStart = drive.length;
  drive.push(box("PropHub", [hubX, shaftY - 0.1, -0.1], [hubX + 0.12, shaftY + 0.1, 0.1], "planks"));
  drive.push(box("Spinner", [hubX + 0.12, shaftY - 0.05, -0.05], [hubX + 0.18, shaftY + 0.05, 0.05], "iron"));
  const bladeTilt = 20;
  [0, 180].forEach((a, i) => {
    const c = [hubX + 0.06, shaftY, 0];
    const segs = [[0.06, 0.34, 0.14], [0.34, 0.85, 0.2], [0.85, 1.12, 0.16], [1.12, 1.2, 0.1]];
    segs.forEach(([r0, r1, w], k) => {
      drive.push(beam(`Blade${i}${k}`, [c[0], c[1] + r0, c[2]], [c[0], c[1] + r1, c[2]], 0.05, "planks", w, 0));
      const e = drive[drive.length - 1];
      // Legg bladet rundt navet: dreies om akselen (design-X) med origo i navet
      e.rotationOrigin = c.map(px);
      e.from = [px(c[0]), px(c[1] + r0), px(c[2] - w / 2)];
      e.to = [px(c[0] + 0.05), px(c[1] + r1), px(c[2] + w / 2)];
      delete e.rotationZ; delete e.rotationY;
      e.rotationX = a + bladeTilt;
      e.faces = faces("planks", 0.05, r1 - r0, w);
    });
  });
  // Nav, spinner og blader flyttes inn i gruppa «Propeller» (animasjonen «propspin» dreier den om X)
  drive.push(group("Propeller", drive.splice(propStart), { rotationOrigin: [hubX + 0.06, shaftY, 0].map(px) }));
}

// ---------------------------------------------------------------- S7: hale + ror
const tail = [];
{
  const y = TOP_T + 0.03;
  // Haleflate (stabilisator) + høyderor bak
  tail.push(beam("StabSpar", [TAIL + 0.55, y, -1.25], [TAIL + 0.55, y, 1.25], 0.06, "wood"));
  tail.push(box("StabL", [TAIL - 0.02, y - 0.025, -1.25], [TAIL + 0.75, y + 0.025, -0.06], "fabric"));
  tail.push(box("StabR", [TAIL - 0.02, y - 0.025, 0.06], [TAIL + 0.75, y + 0.025, 1.25], "fabric"));
  tail.push(box("StabTipL", [TAIL + 0.1, y - 0.02, -1.35], [TAIL + 0.6, y + 0.02, -1.25], "fabric"));
  tail.push(box("StabTipR", [TAIL + 0.1, y - 0.02, 1.25], [TAIL + 0.6, y + 0.02, 1.35], "fabric"));
  tail.push(box("ElevatorL", [TAIL - 0.4, y - 0.02, -1.15], [TAIL - 0.04, y + 0.02, -0.08], "fabric"));
  tail.push(box("ElevatorR", [TAIL - 0.4, y - 0.02, 0.08], [TAIL - 0.04, y + 0.02, 1.15], "fabric"));
  tail.push(box("ElevatorEdge", [TAIL - 0.43, y - 0.03, -1.15], [TAIL - 0.39, y + 0.03, 1.15], "wood"));
  // Finne i tre trinn (grovt trekantet) + ror
  tail.push(box("Fin0", [TAIL - 0.02, y, -0.025], [TAIL + 0.7, y + 0.2, 0.025], "fabric"));
  tail.push(box("Fin1", [TAIL - 0.02, y + 0.2, -0.025], [TAIL + 0.42, y + 0.42, 0.025], "fabric"));
  tail.push(box("Fin2", [TAIL - 0.02, y + 0.42, -0.025], [TAIL + 0.18, y + 0.6, 0.025], "fabric"));
  tail.push(box("RudderPost", [TAIL - 0.05, botY(TAIL) - 0.05, -0.04], [TAIL + 0.02, y + 0.72, 0.04], "wood"));
  tail.push(box("Rudder", [TAIL - 0.5, botY(TAIL) - 0.1, -0.025], [TAIL - 0.05, y + 0.66, 0.025], "fabric"));
  tail.push(box("RudderEdge", [TAIL - 0.54, botY(TAIL) - 0.1, -0.035], [TAIL - 0.49, y + 0.6, 0.035], "wood"));
  // Styrekabler fra førerplassen til halen
  for (const s of [-1, 1]) {
    const side = s < 0 ? "L" : "R";
    tail.push(beam(`Cable${side}`, [0.3, BOT + 0.25, s * (W + 0.07)], [TAIL + 0.05, y - 0.02, s * 0.2], 0.022, "rope"));
  }
}

// ---------------------------------------------------------------- S8: seter
const seats = [];
// Piloten sitter foran (spak + styrekabler), passasjeren bak ved lastehylla
const PILOT = [0.4, BOT + 0.3, 0], PASSENGER = [-0.9, BOT + 0.3, 0];
{
  const chair = (name, [x, y, z], cushion) => {
    seats.push(box(`${name}Pan`, [x - 0.25, y - 0.06, z - 0.25], [x + 0.25, y, z + 0.25], "planks"));
    seats.push(box(`${name}Cushion`, [x - 0.22, y, z - 0.22], [x + 0.2, y + 0.05, z + 0.22], cushion));
    seats.push(box(`${name}Back`, [x - 0.3, y - 0.06, z - 0.25], [x - 0.24, y + 0.55, z + 0.25], "planks"));
    seats.push(box(`${name}BackPad`, [x - 0.24, y + 0.12, z - 0.2], [x - 0.2, y + 0.48, z + 0.2], "leather"));
    [-1, 1].forEach((s, i) => seats.push(box(`${name}Leg${i}`, [x - 0.04, BOT + 0.07, z + s * 0.18 - 0.03], [x + 0.04, y - 0.06, z + s * 0.18 + 0.03], "wood")));
  };
  chair("Pilot", PILOT, "pelt");
  chair("Passenger", PASSENGER, "leather");
  // Sammenbrettet ullteppe hengt over ryggen på passasjersetet
  {
    const [x, y] = PASSENGER;
    seats.push(box("BlanketBack", [x - 0.33, y + 0.3, -0.21], [x - 0.3, y + 0.58, 0.21], "blanket"));
    seats.push(box("BlanketTop", [x - 0.34, y + 0.58, -0.21], [x - 0.19, y + 0.62, 0.21], "blanket"));
    seats.push(box("BlanketFront", [x - 0.22, y + 0.34, -0.21], [x - 0.19, y + 0.58, 0.21], "blanket"));
  }
  seats.push(beam("Stick", [PILOT[0] + 0.45, BOT + 0.07, 0], [PILOT[0] + 0.4, BOT + 0.65, 0], 0.04, "wood", 0.04));
  seats.push(box("StickGrip", [PILOT[0] + 0.36, BOT + 0.62, -0.035], [PILOT[0] + 0.44, BOT + 0.72, 0.035], "twine"));
  // Setene: rytteren sitter på AP-et (midt på putene), med ansiktet framover (+X)
  seats.push(apBox("PilotSeat", "PilotAP", [PILOT[0], PILOT[1] + 0.05 + 0.3, 0], [0.5, 0.6, 0.5], "seat"));
  seats.push(apBox("PassengerSeat", "PassengerAP", [PASSENGER[0], PASSENGER[1] + 0.05 + 0.3, 0], [0.5, 0.6, 0.5], "seat"));
}
// Seteklossen skal starte på puta, ikke midt i den
for (const e of seats) if (e.attachmentpoints) { e.from[1] = px(PILOT[1] + 0.05); e.to[1] = px(PILOT[1] + 0.65); }
// Lasteplass på hylla bak piloten: Ctrl+høyreklikk med kiste/trunk fester den (vanilla rideableaccessories).
// Kista henges inn i denne klossen (stepParentTo) fra hjørnet, så klossen er én blokk bred og står på hylla
const SHELF_TOP = botY(-1.55) + 0.1;
frame.push(apBox("CargoBox", "CargoAP", [-1.9, SHELF_TOP + 0.01 + 0.3, 0], [1.0, 0.6, 1.0]));

// ---------------------------------------------------------------- slitasje / hjemmesnekret
{
  // Nyere reparasjonsplanke spikret skjevt over motordekselet (venstre), og en blikkplate på høyre side
  frame.push(box("RepairPlank", [1.3, BOT + 0.2, -W - 0.08], [2.05, BOT + 0.33, -W - 0.05], "planks2", { rot: [0, 0, -4] }));
  frame.push(box("TinPatch", [-0.5, BOT + 0.08, W + 0.05], [-0.15, BOT + 0.3, W + 0.065], "rust", { rot: [0, 0, 3] }));
  // Nagler i blikkplata
  [[-0.47, 0.11], [-0.18, 0.11], [-0.47, 0.27], [-0.18, 0.27]].forEach(([x, y], i) =>
    frame.push(box(`TinRivet${i}`, [x - 0.012, BOT + y - 0.012, W + 0.065], [x + 0.012, BOT + y + 0.012, W + 0.075], "iron")));
  // Ekstra tausurring på skjøtene fremst i skroget
  for (const s of [-1, 1]) [1.2, COCKPIT_BACK].forEach((x, i) =>
    frame.push(box(`FrameLash${s < 0 ? "L" : "R"}${i}`, [x - 0.07, TOP - 0.07, s * W - 0.07], [x + 0.07, TOP + 0.07, s * W + 0.07], "rope")));
  // Rustne eksosrør og sotstriper nedover dekselet
  for (const e of engine) if (/^Exhaust|^Head/.test(e.name)) for (const fc of Object.values(e.faces)) fc.texture = "#rust";
  engine.push(box("SootStreak", [NOSE - 0.5, TOP - 0.25, W + 0.05], [NOSE - 0.04, TOP - 0.1, W + 0.06], "soot", { rot: [0, 0, 6] }));
  // Tanken er lappet med et jernbånd og en tøyfille rundt lokket
  engine.push(box("CapRag", [FUEL_CAP[0] - 0.07, FUEL_CAP[1] - 0.12, -0.07], [FUEL_CAP[0] + 0.07, FUEL_CAP[1] - 0.05, 0.07], "patch2"));
  // Haleflata har også fått en lapp
  tail.push(box("StabPatch", [TAIL + 0.1, TOP_T + 0.055, 0.35], [TAIL + 0.45, TOP_T + 0.062, 0.7], "patch", { rot: [0, 6, 0] }));
}

// ---------------------------------------------------------------- utdata
// Må speiles i entities/wright.json og wrightconstruction.json (client.textures)
const textures = {
  wood: "game:block/wood/debarked/oak",
  planks: "game:block/wood/planks/aged1",
  planks2: "game:block/wood/planks/birch1",
  aged: "game:block/wood/debarked/aged",
  fabric: "game:block/cloth/linen/normal1",
  patch: "game:block/cloth/linen/brown",
  patch2: "game:block/cloth/burlap/sides",
  patch3: "game:block/cloth/linen/village/darkbrown",
  rope: "game:block/cloth/reedrope",
  twine: "game:block/cloth/reedrope",
  iron: "game:block/metal/sheet/iron1",
  rust: "game:block/metal/corroded/rusty1",
  soot: "game:block/metal/sheet/lead1",
  chain: "game:block/metal/tarnished/chain-rust",
  copper: "game:block/metal/sheet/copper3",
  leather: "game:block/cloth/leather/smallstiches",
  pelt: "game:block/cloth/pelt",
  blanket: "game:block/cloth/wool/green2"
};

const steps = [
  ["S1Frame", frame], ["S2Gear", gear], ["S3LowerWing", lowerWing], ["S4UpperWing", upperWing],
  ["S5Engine", engine], ["S6Drive", drive], ["S7Tail", tail], ["S8Seat", seats]
];

const spin = (code, el, axis) => ({
  name: code, code, quantityframes: 31, onActivityStopped: "Stop", onAnimationEnd: "Repeat",
  // VS krever alle tre rotasjonsaksene i en rotasjons-keyframe (Animation.lerpKeyFrameElement leser .Value på X/Y/Z)
  keyframes: [0, 30].map(frame => ({ frame, elements: { [el]: { rotationX: 0, rotationY: 0, rotationZ: 0, ["rotation" + axis]: frame * 12 } } }))
});
const wheelSpin = { ...spin("wheelspin", "WheelL", "Z") };
wheelSpin.keyframes.forEach(k => { k.elements.WheelR = { ...k.elements.WheelL }; });

function shapeWith(includeSite) {
  const children = [];
  if (includeSite) children.push(group("S0Site", site));
  for (const [name, els] of steps) children.push(group(name, els));
  return {
    textureWidth: 16, textureHeight: 16,
    textures,
    ...(includeSite ? {} : { animations: [spin("propspin", "Propeller", "X"), wheelSpin] }),
    elements: [{
      name: "Root", from: [8, 0, 8], to: [8, 0, 8], rotationOrigin: [8, 0, 8], rotationY: 180,
      faces: {}, children
    }]
  };
}

// Z-fighting: flater i samme plan som vender samme vei flimrer i spillet. For hvert slikt par skyves ÉN av
// flatene 0.012 blokk (≈ 1/5 tekstur-piksel, usynlig) utover — fra klossen som er justert minst fra før —
// og så sjekkes det på nytt til ingen par er igjen. Elementene deles mellom de to shapene, så det holder
// å rette byggeplassen (som inneholder alt).
const { findCoplanar } = require("./zfight_check");
// Flatenavn → [from/to, akse] i elementets lokale ramme (samme som preview/zfight_check)
const FACE_AXIS = { west: ["from", 0], east: ["to", 0], down: ["from", 1], up: ["to", 1], north: ["from", 2], south: ["to", 2] };
const pushed = new Map(); // element → antall flytt
function resolveZFighting(shape) {
  const step = px(0.012);
  for (let round = 0; round < 60; round++) {
    const { hits } = findCoplanar(shape);
    if (!hits.length) return round;
    const done = new Set();
    for (const { a, b } of hits) {
      if (done.has(a.el) || done.has(b.el)) continue;
      const f = (pushed.get(a.el) || 0) < (pushed.get(b.el) || 0) ? a : b;
      const [side, axis] = FACE_AXIS[f.name.split(":").pop()];
      f.el[side][axis] = r3(f.el[side][axis] + (side === "to" ? step : -step));
      pushed.set(f.el, (pushed.get(f.el) || 0) + 1);
      done.add(a.el); done.add(b.el);
    }
  }
  throw new Error("Z-fighting ble ikke løst på 60 runder: " + findCoplanar(shape).hits.slice(0, 5).map(h => h.a.name + " ↔ " + h.b.name).join(", "));
}
const rounds = resolveZFighting(shapeWith(true));
const worst = [...pushed].sort((x, y) => y[1] - x[1]);
console.log(`Z-fighting løst på ${rounds} runder: ${pushed.size} elementer justert, mest ${worst.slice(0, 6).map(([e, n]) => e.name + "×" + n).join(", ")}`);

const outDir = path.join(__dirname, "..", "Wright", "assets", "wright", "shapes", "entity");
const write = (file, obj) => {
  fs.writeFileSync(path.join(outDir, file), JSON.stringify(obj, null, "\t") + "\n");
  let n = 0;
  const count = els => els.forEach(e => { if (Object.keys(e.faces).length) n++; if (e.children) count(e.children); });
  count(obj.elements);
  console.log(`Skrev ${file} (${n} synlige elementer)`);
};
write("wright.json", shapeWith(false));
const entDir = path.join(__dirname, "..", "Wright", "assets", "wright", "entities");
for (const ef of ["wright.json", "wrightconstruction.json"]) {
  const p = path.join(entDir, ef), src = fs.readFileSync(p, "utf8");
  // Finn client.textures-blokka og erstatt den (klammer telles, JSON-en har kommentarer)
  const start = src.indexOf("textures: {");
  if (start < 0) throw new Error("fant ikke textures-blokka i " + ef);
  let depth = 0, end = start;
  for (let k = src.indexOf("{", start); k < src.length; k++) {
    if (src[k] === "{") depth++;
    else if (src[k] === "}" && --depth === 0) { end = k + 1; break; }
  }
  const NL = String.fromCharCode(10), TAB = String.fromCharCode(9);
  const block = "textures: {" + NL + Object.entries(textures).map(([k, v]) => TAB.repeat(3) + `"${k}": { base: "${v}" }`).join("," + NL) + NL + TAB.repeat(2) + "}";
  fs.writeFileSync(p, src.slice(0, start) + block + src.slice(end));
}
write("construction.json", shapeWith(true));
module.exports = { shapeWith };
