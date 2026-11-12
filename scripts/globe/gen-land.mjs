// Source data: Natural Earth 1:110m land polygons (ne_110m_land.geojson, https://www.naturalearthdata.com/), which is in the public domain
// (https://www.naturalearthdata.com/about/terms-of-use/). The output is the same format and size as v2rayN.Desktop/Assets/globe-land.txt.
// It is NOT wired into the app: replacing the asset changes the globe slightly.
//   node scripts/globe/gen-land.mjs ne_110m_land.geojson out.txt [current globe-land.txt]
// Mask format: 180 latitude rows, round(360*cos(lat)) cells per row, one bit per cell; a cell is land when its centre lies inside
// a Natural Earth 1:110m land polygon. Agreement with the asset that ships today: 96.5% of cells (most of the difference is above 60 N).
import fs from 'node:fs';

const [, , geoPath, outPath, currentPath] = process.argv;
const gj = JSON.parse(fs.readFileSync(geoPath, 'utf8'));

// polygons: [ [outerRing, hole1, hole2...], ... ] with rings as [lon, lat] arrays
const polys = [];
for (const f of gj.features) {
  const g = f.geometry;
  if (g.type === 'Polygon') polys.push(g.coordinates);
  else if (g.type === 'MultiPolygon') polys.push(...g.coordinates);
}
const bbox = polys.map(p => {
  let a = 180, b = -180, c = 90, d = -90;
  for (const [x, y] of p[0]) { if (x < a) a = x; if (x > b) b = x; if (y < c) c = y; if (y > d) d = y; }
  return [a, b, c, d];
});

function inRing(x, y, ring) {
  let inside = false;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const [xi, yi] = ring[i], [xj, yj] = ring[j];
    if ((yi > y) !== (yj > y) && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}
function isLand(lon, lat) {
  for (let k = 0; k < polys.length; k++) {
    const [a, b, c, d] = bbox[k];
    if (lon < a || lon > b || lat < c || lat > d) continue;
    const p = polys[k];
    if (!inRing(lon, lat, p[0])) continue;
    let hole = false;
    for (let h = 1; h < p.length; h++) if (inRing(lon, lat, p[h])) { hole = true; break; }
    if (!hole) return true;
  }
  return false;
}

const rows = [];
let total = 0;
for (let row = 0; row < 180; row++) {
  const lat = row - 89.5;
  const cells = Math.max(1, Math.floor(360 * Math.cos((lat * Math.PI) / 180) + 0.5));
  const r = [];
  for (let i = 0; i < cells; i++) r.push(isLand(((i + 0.5) * 360) / cells - 180, lat));
  rows.push(r); total += cells;
}
const bytes = Buffer.alloc(Math.ceil(total / 8));
let cell = 0;
for (const r of rows) for (const v of r) { if (v) bytes[cell >> 3] |= 1 << (cell % 8); cell++; }
fs.writeFileSync(outPath, bytes.toString('base64'));
const landCount = rows.flat().filter(Boolean).length;
console.log(`cells ${total}, land ${landCount} (${((100 * landCount) / total).toFixed(1)}%), file ${fs.statSync(outPath).size} bytes`);

if (currentPath) {
  const cur = Buffer.from(fs.readFileSync(currentPath, 'utf8').trim(), 'base64');
  let agree = 0, both = 0, either = 0, curLand = 0;
  const perBand = {};
  cell = 0;
  for (let row = 0; row < 180; row++) {
    for (let i = 0; i < rows[row].length; i++, cell++) {
      const c = !!(cur[cell >> 3] & (1 << (cell % 8))), n = rows[row][i];
      if (c === n) agree++;
      if (c && n) both++;
      if (c || n) either++;
      if (c) curLand++;
      const band = Math.floor((row - 90) / 30) * 30;
      (perBand[band] ??= { cells: 0, diff: 0 }).cells++;
      if (c !== n) perBand[band].diff++;
    }
  }
  console.log(`agreement with the current mask: ${((100 * agree) / total).toFixed(2)}% of cells; land overlap (IoU) ${((100 * both) / either).toFixed(1)}%; current land cells ${curLand}`);
  for (const [b, v] of Object.entries(perBand).sort((a, b) => a[0] - b[0])) console.log(`  lat ${String(b).padStart(4)}..${Number(b) + 30}: ${(100 * v.diff / v.cells).toFixed(1)}% of cells differ`);
}
