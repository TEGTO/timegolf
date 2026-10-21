import {BufferAttribute, BufferGeometry, Mesh, MeshStandardMaterial, type Group, type Texture} from 'three';
import type {LevelDto} from '../types';
import type {Palette} from './palettes';
import {chevronTexture, type TextureSet} from './textures';

const TIER_STEP = 0.5;
/** How far the cliff skirt reaches below the lowest tier. */
const SKIRT_DEPTH = 1.4;
// Downhill edge of a ramp is shaded darker so the incline reads even where lighting is flat.
const SLOPE_SHADE_DOWN = 0.82;
// Cliff sides darken toward their foot so stacked tiers read apart: this shade at the skirt base, full from the top y up.
const CLIFF_SHADE_FOOT = 0.62;
const CLIFF_SHADE_TOP_Y = 0.5;
/** Cells per grass texture repeat (one light + one dark stripe). */
const GRASS_TILE_CELLS = 2;
const FLOOR_ROUGHNESS = 0.95;
const CHEVRON_OPACITY = 0.55;
// Pulls the chevrons in front of the ramp they lie on without visibly lifting them.
const CHEVRON_POLYGON_OFFSET = -2;
/** Cliff faces lower than this are skipped. */
const MIN_CLIFF = 1e-3;

const SLOPE_DIR: Record<string, [number, number]> = {'>': [1, 0], '<': [-1, 0], v: [0, 1], '^': [0, -1]};

// Corner order per cell: (x0,z0) (x1,z0) (x0,z1) (x1,z1).
const CORNER = [
  [0, 0],
  [1, 0],
  [0, 1],
  [1, 1]
];

/** Two up-facing triangles per cell, as corner indices. */
const CELL_TRIANGLES = [0, 2, 1, 1, 2, 3];

// Per edge: own corners A,B (ordered so the side face points outward), neighbor offset, neighbor's matching corners.
const EDGES: [number, number, number, number, number, number][] = [
  [0, 1, 0, -1, 2, 3],
  [1, 3, 1, 0, 0, 2],
  [3, 2, 0, 1, 1, 0],
  [2, 0, -1, 0, 3, 1]
];

export type Terrain = {
  /** Surface height; the skirt base off the course. */
  heightAt(x: number, z: number): number;
  /** Lowest tier top. */
  minY: number;
  /** Bottom of the cliff skirt. */
  baseY: number;
};

class GeoBuilder {
  pos: number[] = [];
  uv: number[] = [];
  color: number[] = [];

  vertex(x: number, y: number, z: number, u: number, v: number, shade = 1) {
    this.pos.push(x, y, z);
    this.uv.push(u, v);
    this.color.push(shade, shade, shade);
  }

  build(withColor = false) {
    const g = new BufferGeometry();
    g.setAttribute('position', new BufferAttribute(new Float32Array(this.pos), 3));
    g.setAttribute('uv', new BufferAttribute(new Float32Array(this.uv), 2));
    if (withColor) g.setAttribute('color', new BufferAttribute(new Float32Array(this.color), 3));
    g.computeVertexNormals();
    return g;
  }
}

/** Tiered floor, ramps with downhill chevrons and cliff sides, added to `group`. */
export function buildTerrain(level: LevelDto, pal: Palette, tex: TextureSet, group: Group): Terrain {
  const {width: w, height: h, cells, heights} = level;

  const at = (cx: number, cz: number) => (cx < 0 || cz < 0 || cx >= w || cz >= h ? '.' : cells[cz * w + cx]);
  const tierY = (cx: number, cz: number) => -TIER_STEP * (heights[cz * w + cx] ?? 0);
  const isSlope = (c: string) => c in SLOPE_DIR;
  const isFlatFloor = (c: string) => c !== '.' && !isSlope(c);

  let minY = 0;
  for (let i = 0; i < w * h; i++) if (cells[i] !== '.') minY = Math.min(minY, -TIER_STEP * heights[i]);
  const baseY = minY - SKIRT_DEPTH;

  const corners = new Float32Array(w * h * 4);
  // Vertex shade per corner: 1 on flat floor, darkening down the whole ramp run (not per cell, which reads as a checkerboard).
  const rampShade = new Float32Array(w * h * 4).fill(1);
  for (let cz = 0; cz < h; cz++)
    for (let cx = 0; cx < w; cx++) {
      const c = at(cx, cz);
      const i = (cz * w + cx) * 4;
      if (!isSlope(c)) {
        corners.fill(tierY(cx, cz), i, i + 4);
        continue;
      }
      // A ramp is a run of slope cells; interpolate over the whole run so the surface meets both tiers seamlessly.
      const [dx, dz] = SLOPE_DIR[c];
      let back = 0;
      while (at(cx - (back + 1) * dx, cz - (back + 1) * dz) === c) back++;
      let fwd = 0;
      while (at(cx + (fwd + 1) * dx, cz + (fwd + 1) * dz) === c) fwd++;
      const bx = cx - (back + 1) * dx;
      const bz = cz - (back + 1) * dz;
      const fx = cx + (fwd + 1) * dx;
      const fz = cz + (fwd + 1) * dz;
      const yBack = isFlatFloor(at(bx, bz)) ? tierY(bx, bz) : tierY(cx, cz);
      const yFront = isFlatFloor(at(fx, fz)) ? tierY(fx, fz) : yBack - TIER_STEP;
      const run = back + fwd + 1;
      const yUp = yBack + ((yFront - yBack) * back) / run;
      const yDown = yBack + ((yFront - yBack) * (back + 1)) / run;
      for (let k = 0; k < 4; k++) {
        const [ox, oz] = CORNER[k];
        const along = dx !== 0 ? (dx > 0 ? ox : 1 - ox) : dz > 0 ? oz : 1 - oz;
        corners[i + k] = yUp + (yDown - yUp) * along;
        rampShade[i + k] = 1 + ((SLOPE_SHADE_DOWN - 1) * (yBack - corners[i + k])) / (yBack - yFront || 1);
      }
    }

  const heightAt = (x: number, z: number) => {
    const cx = Math.floor(x);
    const cz = Math.floor(z);
    if (at(cx, cz) === '.') return baseY;
    const i = (cz * w + cx) * 4;
    const fx = x - cx;
    const fz = z - cz;
    const top = corners[i] + (corners[i + 1] - corners[i]) * fx;
    const bottom = corners[i + 2] + (corners[i + 3] - corners[i + 2]) * fx;
    return top + (bottom - top) * fz;
  };

  const grass = new GeoBuilder();
  const slope = new GeoBuilder();
  const chevrons = new GeoBuilder();
  const sides = new GeoBuilder();
  const shade = (y: number) =>
    CLIFF_SHADE_FOOT + (1 - CLIFF_SHADE_FOOT) * Math.min(1, (y - baseY) / (CLIFF_SHADE_TOP_Y - baseY));

  for (let cz = 0; cz < h; cz++)
    for (let cx = 0; cx < w; cx++) {
      const c = at(cx, cz);
      if (c === '.') continue;
      const i = (cz * w + cx) * 4;
      const dir = SLOPE_DIR[c];
      // Sand is drawn as grass here; the bunker decal on top gives it a rounded, soft edge.
      const target = dir ? slope : grass;
      for (const k of CELL_TRIANGLES) {
        const [ox, oz] = CORNER[k];
        const x = cx + ox;
        const z = cz + oz;
        const u = x / GRASS_TILE_CELLS;
        const v = z / GRASS_TILE_CELLS;
        if (!dir) {
          target.vertex(x, corners[i + k], z, u, v);
          continue;
        }
        // 0 at the uphill edge, 1 at the downhill edge; the chevron tile's +u then points downhill.
        const along = dir[0] !== 0 ? (dir[0] > 0 ? ox : 1 - ox) : dir[1] > 0 ? oz : 1 - oz;
        const across = dir[0] !== 0 ? oz : ox;
        target.vertex(x, corners[i + k], z, u, v, rampShade[i + k]);
        chevrons.vertex(x, corners[i + k], z, along, across);
      }

      for (const [a, b, nx, nz, na, nb] of EDGES) {
        const n = at(cx + nx, cz + nz);
        const ni = ((cz + nz) * w + cx + nx) * 4;
        const topA = corners[i + a];
        const topB = corners[i + b];
        const botA = n === '.' ? baseY : Math.min(topA, corners[ni + na]);
        const botB = n === '.' ? baseY : Math.min(topB, corners[ni + nb]);
        if (topA - botA < MIN_CLIFF && topB - botB < MIN_CLIFF) continue;
        const ax = cx + CORNER[a][0];
        const az = cz + CORNER[a][1];
        const bx = cx + CORNER[b][0];
        const bz = cz + CORNER[b][1];
        sides.vertex(ax, topA, az, ax + az, topA, shade(topA));
        sides.vertex(bx, topB, bz, bx + bz, topB, shade(topB));
        sides.vertex(ax, botA, az, ax + az, botA, shade(botA));
        sides.vertex(bx, topB, bz, bx + bz, topB, shade(topB));
        sides.vertex(bx, botB, bz, bx + bz, botB, shade(botB));
        sides.vertex(ax, botA, az, ax + az, botA, shade(botA));
      }
    }

  const addFloor = (geo: GeoBuilder, map: Texture, shaded = false) => {
    if (geo.pos.length === 0) return;
    const material = new MeshStandardMaterial({map, roughness: FLOOR_ROUGHNESS, vertexColors: shaded});
    const mesh = new Mesh(geo.build(shaded), material);
    mesh.receiveShadow = true;
    group.add(mesh);
  };
  addFloor(grass, tex.grass);
  addFloor(slope, tex.grass, true);
  if (chevrons.pos.length > 0) {
    const marks = new Mesh(
      chevrons.build(),
      new MeshStandardMaterial({
        map: chevronTexture(),
        transparent: true,
        opacity: CHEVRON_OPACITY,
        depthWrite: false,
        polygonOffset: true,
        polygonOffsetFactor: CHEVRON_POLYGON_OFFSET,
        polygonOffsetUnits: CHEVRON_POLYGON_OFFSET,
        roughness: 1
      })
    );
    marks.receiveShadow = true;
    group.add(marks);
  }
  if (sides.pos.length > 0) {
    const cliff = new Mesh(
      sides.build(true),
      new MeshStandardMaterial({color: pal.cliff, vertexColors: true, roughness: 1})
    );
    // Faces turned away from the sun are exactly what a raised tier's shadow needs.
    cliff.castShadow = cliff.receiveShadow = true;
    group.add(cliff);
  }

  return {heightAt, minY, baseY};
}
