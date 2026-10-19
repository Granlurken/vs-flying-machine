// Checks that the generated assets in git match what the generators produce now.
// Run: node tools/check_generated.js
// Runs flyingmachine_model.js, item_shapes.js and dash_textures.js in a temporary copy (the repo is left alone) and
// compares every file they wrote. PNGs are compared by pixels, since zlib versions compress differently.
const fs = require("fs");
const os = require("os");
const path = require("path");
const zlib = require("zlib");
const { execFileSync } = require("child_process");

const repo = path.join(__dirname, "..");
const assets = path.join("FlyingMachine", "assets", "flyingmachine");
const generated = [path.join(assets, "shapes"), path.join(assets, "entities"), path.join(assets, "textures", "dash")];

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "flyingmachine-gen-"));
try {
  fs.cpSync(path.join(repo, "tools"), path.join(tmp, "tools"), { recursive: true });
  fs.cpSync(path.join(repo, assets), path.join(tmp, assets), { recursive: true });
  for (const script of ["flyingmachine_model.js", "item_shapes.js", "dash_textures.js"]) {
    execFileSync(process.execPath, [path.join(tmp, "tools", script)], { stdio: ["ignore", "ignore", "inherit"] });
  }

  const stale = [];
  for (const dir of generated) {
    for (const file of walk(path.join(tmp, dir))) {
      const rel = path.relative(tmp, file);
      const committed = path.join(repo, rel);
      if (!fs.existsSync(committed)) stale.push(rel + " (missing)");
      else if (!same(fs.readFileSync(committed), fs.readFileSync(file), rel)) stale.push(rel);
    }
  }

  if (stale.length) {
    console.error("Generated assets are out of date. Rerun the generators and commit the result:");
    for (const rel of stale) console.error("  " + rel);
    process.exitCode = 1;
  } else {
    console.log("Generated assets are up to date.");
  }
} finally {
  fs.rmSync(tmp, { recursive: true, force: true });
}

function* walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, entry.name);
    if (entry.isDirectory()) yield* walk(p);
    else yield p;
  }
}

function same(a, b, name) {
  if (a.equals(b)) return true;
  // Windows-checkout med core.autocrlf gir CRLF i arbeidskopien; generatorene skriver LF
  if (!name.endsWith(".png")) return a.toString("utf8").replace(/\r\n/g, "\n") === b.toString("utf8").replace(/\r\n/g, "\n");
  const pa = pixels(a), pb = pixels(b);
  return pa.header.equals(pb.header) && pa.data.equals(pb.data);
}

function pixels(png) {
  let header = Buffer.alloc(0);
  const idat = [];
  for (let i = 8; i < png.length;) {
    const len = png.readUInt32BE(i), type = png.toString("ascii", i + 4, i + 8);
    if (type === "IHDR") header = png.subarray(i + 8, i + 8 + len);
    if (type === "IDAT") idat.push(png.subarray(i + 8, i + 8 + len));
    i += 12 + len;
  }
  return { header, data: zlib.inflateSync(Buffer.concat(idat)) };
}
