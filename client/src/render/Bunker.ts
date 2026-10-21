import {Mesh, MeshStandardMaterial, PlaneGeometry, type Group, type Texture} from 'three';
import type {LevelDto} from '../types';
import {RENDER_ORDER, SUN_DIR} from './constants';
import type {Palette} from './palettes';
import {canvas} from './textures';

/** Decal texels per cell. */
const PX = 32;
/** Empty cells around each region, so the blur fades out inside the decal. */
const MARGIN = 1;
// Gaussian blur of the sand cells (in cells): rounds the rasterized staircase back into the designed oval.
const SIGMA = 0.45;
const SQRT2_SIGMA = Math.SQRT2 * SIGMA;
// Horizontal direction toward the sun: the bank nearest the sun falls into shade.
const SUN_X = SUN_DIR.x / Math.hypot(SUN_DIR.x, SUN_DIR.z);
const SUN_Z = SUN_DIR.z / Math.hypot(SUN_DIR.x, SUN_DIR.z);
// Field levels (0 outside .. 1 deep inside): where the sand edge fades in, and where the bowl reaches full depth.
const EDGE_FROM = 0.46;
const EDGE_TO = 0.54;
const DEPTH_FROM = 0.5;
const DEPTH_TO = 0.85;
const BANK_LIGHT = 0.3;
const BANK_CONTRAST = 1.6;
const RIM_DARKEN = 0.1;
const RAKE_AMPLITUDE = 0.022;
const RAKE_FREQUENCY = 70;
// Rake lines only in the flat middle, not on the banks.
const RAKE_MIN_DEPTH = 0.25;
const GRAIN = 0.09;
// A faint dark lip on the grass just outside the sand.
const LIP_ALPHA = 0.1;
const LIP_FROM = 0.42;
const LIP_TO = 0.5;
const DECAL_LIFT = 0.003;
const DECAL_POLYGON_OFFSET = -1;
/** Field contributions below this are skipped. */
const MIN_COVER = 1e-4;

// Abramowitz–Stegun 7.1.26, plenty for shading.
function erf(x: number) {
  const s = Math.sign(x);
  const a = Math.abs(x);
  const t = 1 / (1 + 0.3275911 * a);
  const y =
    1 -
    ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.exp(-a * a);
  return s * y;
}

const smooth = (a: number, b: number, x: number) => {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
};

/** Share of a Gaussian centred at u falling into cell [c, c + 1]. */
const cover = (u: number, c: number) => 0.5 * (erf((c + 1 - u) / SQRT2_SIGMA) - erf((c - u) / SQRT2_SIGMA));

function components(level: LevelDto) {
  const {width: w, height: h, cells} = level;
  const seen = new Uint8Array(w * h);
  const out: number[][] = [];
  for (let i = 0; i < w * h; i++) {
    if (cells[i] !== 's' || seen[i]) continue;
    const list: number[] = [];
    const stack = [i];
    seen[i] = 1;
    while (stack.length > 0) {
      const j = stack.pop()!;
      list.push(j);
      const cx = j % w;
      const cz = (j - cx) / w;
      for (const [nx, nz] of [
        [cx + 1, cz],
        [cx - 1, cz],
        [cx, cz + 1],
        [cx, cz - 1]
      ]) {
        const k = nz * w + nx;
        if (nx < 0 || nz < 0 || nx >= w || nz >= h || seen[k] || cells[k] !== 's') continue;
        seen[k] = 1;
        stack.push(k);
      }
    }
    out.push(list);
  }
  return out;
}

/** Soft, slightly sunken-looking bunker decal per connected sand region; drawn once at level build. */
export function buildBunkers(level: LevelDto, heightAt: (x: number, z: number) => number, pal: Palette, parent: Group) {
  const w = level.width;
  const hex = parseInt(pal.sand.slice(1), 16);
  const sandR = hex >> 16;
  const sandG = (hex >> 8) & 255;
  const sandB = hex & 255;
  const textures: Texture[] = [];
  for (const list of components(level)) {
    let minX = Infinity;
    let minZ = Infinity;
    let maxX = -Infinity;
    let maxZ = -Infinity;
    for (const j of list) {
      minX = Math.min(minX, j % w);
      maxX = Math.max(maxX, j % w);
      minZ = Math.min(minZ, Math.floor(j / w));
      maxZ = Math.max(maxZ, Math.floor(j / w));
    }
    const x0 = minX - MARGIN;
    const z0 = minZ - MARGIN;
    const cw = maxX - minX + 1 + 2 * MARGIN;
    const ch = maxZ - minZ + 1 + 2 * MARGIN;
    const occ = new Uint8Array(cw * ch);
    for (const j of list) occ[(Math.floor(j / w) - z0) * cw + (j % w) - x0] = 1;

    const pw = cw * PX;
    const ph = ch * PX;
    const wx = new Float32Array(pw * cw);
    const wz = new Float32Array(ph * ch);
    for (let i = 0; i < pw; i++) for (let c = 0; c < cw; c++) wx[i * cw + c] = cover((i + 0.5) / PX, c);
    for (let j = 0; j < ph; j++) for (let r = 0; r < ch; r++) wz[j * ch + r] = cover((j + 0.5) / PX, r);
    const field = new Float32Array(pw * ph);
    for (let j = 0; j < ph; j++)
      for (let r = 0; r < ch; r++) {
        const fz = wz[j * ch + r];
        if (fz < MIN_COVER) continue;
        for (let c = 0; c < cw; c++) {
          if (!occ[r * cw + c]) continue;
          for (let i = 0; i < pw; i++) field[j * pw + i] += fz * wx[i * cw + c];
        }
      }

    const tex = canvas(
      pw,
      ph,
      (g, rand) => {
        const img = g.createImageData(pw, ph);
        const d = img.data;
        for (let j = 0; j < ph; j++)
          for (let i = 0; i < pw; i++) {
            const k = j * pw + i;
            const v = field[k];
            const gx = (field[j * pw + Math.min(pw - 1, i + 1)] - field[j * pw + Math.max(0, i - 1)]) * (PX / 2);
            const gz = (field[Math.min(ph - 1, j + 1) * pw + i] - field[Math.max(0, j - 1) * pw + i]) * (PX / 2);
            const sandA = smooth(EDGE_FROM, EDGE_TO, v);
            const depth = smooth(DEPTH_FROM, DEPTH_TO, v);
            // Field rises inward, so its gradient leans like the sunken bank's normal.
            const light = Math.max(-1, Math.min(1, (gx * SUN_X + gz * SUN_Z) * SIGMA * BANK_CONTRAST));
            const rake = depth > RAKE_MIN_DEPTH ? RAKE_AMPLITUDE * Math.sin(v * RAKE_FREQUENCY) * depth : 0;
            const shade = 1 + BANK_LIGHT * light - RIM_DARKEN * (1 - depth) + rake + (rand() - 0.5) * GRAIN;
            const lipA = LIP_ALPHA * smooth(LIP_FROM, LIP_TO, v);
            const a = sandA + lipA * (1 - sandA);
            const s = a > 0 ? (sandA * shade) / a : 0;
            d[k * 4] = sandR * s;
            d[k * 4 + 1] = sandG * s;
            d[k * 4 + 2] = sandB * s;
            d[k * 4 + 3] = a * 255;
          }
        g.putImageData(img, 0, 0);
      },
      false
    );
    const cx = x0 + cw / 2;
    const cz = z0 + ch / 2;
    const mesh = new Mesh(
      new PlaneGeometry(cw, ch).rotateX(-Math.PI / 2),
      new MeshStandardMaterial({
        map: tex,
        transparent: true,
        depthWrite: false,
        polygonOffset: true,
        polygonOffsetFactor: DECAL_POLYGON_OFFSET,
        polygonOffsetUnits: DECAL_POLYGON_OFFSET,
        roughness: 1
      })
    );
    mesh.position.set(cx, heightAt(cx, cz) + DECAL_LIFT, cz);
    mesh.receiveShadow = true;
    // Before other ground overlays (ball ring, paths) so the decal never paints over them.
    mesh.renderOrder = RENDER_ORDER.decal;
    parent.add(mesh);
    textures.push(tex);
  }
  return textures;
}
