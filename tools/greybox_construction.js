// Genererer grå-kloss-shapen for byggeplassen (placeholder til den ekte modellen).
// Kjør: node tools/greybox_construction.js  → Wright/assets/wright/shapes/entity/construction.json
// Mål i blokker, (0,0,0) = midten av plassen på bakkenivå. Nesa peker mot +Z.
const fs = require("fs");
const path = require("path");

const u = v => +(8 + v * 16).toFixed(3);
const uy = v => +(v * 16).toFixed(3);

function box(name, x1, y1, z1, x2, y2, z2, tex) {
  const from = [u(x1), uy(y1), u(z1)], to = [u(x2), uy(y2), u(z2)];
  const [w, h, d] = [0, 1, 2].map(i => Math.min(16, Math.max(1, Math.round(to[i] - from[i]))));
  const f = (a, b) => ({ texture: "#" + tex, uv: [0, 0, a, b] });
  return {
    name, from, to,
    faces: { north: f(w, h), east: f(d, h), south: f(w, h), west: f(d, h), up: f(w, d), down: f(w, d) }
  };
}
const group = (name, children) => ({ name, from: [8, 0, 8], to: [8, 0, 8], faces: {}, children });

// Steg 0: bukker + taugjerde rundt 9×9-plassen
const R = 4.4, postH = 1.2, ropeY = 1.0, t = 0.06;
const site = [];
[[-1, -1], [1, -1], [1, 1], [-1, 1]].forEach(([sx, sz], i) =>
  site.push(box("Post" + i, sx * R - 0.08, 0, sz * R - 0.08, sx * R + 0.08, postH, sz * R + 0.08, "debarked")));
site.push(box("RopeN", -R, ropeY, -R - t / 2, R, ropeY + t, -R + t / 2, "rope"));
site.push(box("RopeS", -R, ropeY, R - t / 2, R, ropeY + t, R + t / 2, "rope"));
site.push(box("RopeW", -R - t / 2, ropeY, -R, -R + t / 2, ropeY + t, R, "rope"));
site.push(box("RopeE", R - t / 2, ropeY, -R, R + t / 2, ropeY + t, R, "rope"));
[-2, 1.5].forEach((z, i) => {
  site.push(box("TrestleBar" + i, -0.8, 0.5, z - 0.06, 0.8, 0.62, z + 0.06, "debarked"));
  site.push(box("TrestleLegL" + i, -0.7, 0, z - 0.04, -0.62, 0.5, z + 0.04, "debarked"));
  site.push(box("TrestleLegR" + i, 0.62, 0, z - 0.04, 0.7, 0.5, z + 0.04, "debarked"));
});

// Steg 1: kroppsramme (fire lengdebjelker + tverrbjelker)
const frame = [];
[[-0.4, 0.62], [0.32, 0.62], [-0.4, 1.32], [0.32, 1.32]].forEach(([x, y], i) =>
  frame.push(box("Longeron" + i, x, y, -3.5, x + 0.08, y + 0.08, 2.5, "planks")));
[-3.5, -1, 1.5, 2.42].forEach((z, i) => {
  frame.push(box("CrossLow" + i, -0.4, 0.62, z, 0.4, 0.7, z + 0.08, "planks"));
  frame.push(box("CrossHigh" + i, -0.4, 1.32, z, 0.4, 1.4, z + 0.08, "planks"));
});

// Steg 2: understell (to hjul, aksel, stag)
const gear = [
  box("WheelL", -1.02, 0, 1.15, -0.9, 0.7, 1.85, "planks"),
  box("WheelR", 0.9, 0, 1.15, 1.02, 0.7, 1.85, "planks"),
  box("Axle", -1.0, 0.31, 1.46, 1.0, 0.39, 1.54, "metal"),
  box("StrutL", -0.9, 0.35, 1.46, -0.4, 0.43, 1.54, "planks"),
  box("StrutR", 0.4, 0.35, 1.46, 0.9, 0.43, 1.54, "planks"),
];

// Steg 3: nedre vinge
const lowerWing = [box("LowerWing", -4.3, 0.72, 0.4, 4.3, 0.8, 1.6, "cloth")];

// Steg 4: stag + øvre vinge
const upperWing = [box("UpperWing", -4.3, 2.3, 0.4, 4.3, 2.38, 1.6, "cloth")];
[-3, -1.2, 1.2, 3].forEach((x, i) => [0.55, 1.4].forEach((z, j) =>
  upperWing.push(box(`Strut${i}${j}`, x - 0.04, 0.8, z - 0.04, x + 0.04, 2.3, z + 0.04, "planks"))));

// Steg 5: motor
const engine = [box("Engine", -0.35, 0.7, 2.5, 0.35, 1.3, 3.2, "metal")];

// Steg 6: drivkjede + propell
const drive = [
  box("Hub", -0.1, 0.9, 3.2, 0.1, 1.1, 3.35, "metal"),
  box("Blade", -0.07, 0.1, 3.3, 0.07, 1.9, 3.36, "planks"),
];

// Steg 7: hale + ror
const tail = [
  box("Stabilizer", -1.2, 1.0, -3.8, 1.2, 1.06, -3.2, "cloth"),
  box("Fin", -0.03, 1.06, -3.8, 0.03, 1.9, -3.3, "cloth"),
];

// Steg 8: sete
const seat = [
  box("SeatPan", -0.25, 0.6, -0.6, 0.25, 0.9, -0.1, "cloth"),
  box("SeatBack", -0.25, 0.9, -0.66, 0.25, 1.4, -0.6, "cloth"),
];

const shape = {
  textureWidth: 16, textureHeight: 16,
  textures: {
    planks: "game:block/wood/planks/oak1",
    debarked: "game:block/wood/debarked/oak",
    rope: "game:block/cloth/reedrope",
    metal: "game:block/metal/sheet/iron1",
    cloth: "game:block/cloth/linen/white"
  },
  elements: [group("Root", [
    group("S0Site", site), group("S1Frame", frame), group("S2Gear", gear),
    group("S3LowerWing", lowerWing), group("S4UpperWing", upperWing), group("S5Engine", engine),
    group("S6Drive", drive), group("S7Tail", tail), group("S8Seat", seat)
  ])]
};
const out = path.join(__dirname, "..", "Wright", "assets", "wright", "shapes", "entity", "construction.json");
fs.writeFileSync(out, JSON.stringify(shape, null, "\t") + "\n");
console.log("Skrev " + out);
