import {CanvasTexture, RepeatWrapping, SRGBColorSpace, type Texture} from 'three';
import {paletteIndex, paletteOf, type Palette} from './palettes';

const ANISOTROPY = 4;

const GRASS_PX = 256;
const GRASS_SPECKLES = 2200;
const GRASS_SPECKLE_ALPHA = 0.08;

const STONE_PX = 128;
const STONE_SPECKLES = 600;
const STONE_SPECKLE_ALPHA = 0.15;
const BRICK_ROWS = 4;
const BRICK_H = STONE_PX / BRICK_ROWS;
const BRICK_W = 2 * BRICK_H;
const MORTAR = 'rgba(0,0,0,0.28)';
const MORTAR_PX = 3;

const WOOD_PX = 128;
const WOOD_RING_OUTER = 60;
const WOOD_RING_INNER = 4;
const WOOD_RING_STEP = 7;
const WOOD_RING_PX = 2.5;

const SPECKLE_PX = 2;

const BACKGROUND_W = 4;
const BACKGROUND_H = 256;

const CHEVRON_PX = 128;
const CHEVRON_STROKE = 11;
const CHEVRON_ARM_X = 48;
const CHEVRON_TIP_X = 80;
const CHEVRON_ARM_INSET = 30;

const SAIL_W = 64;
const SAIL_H = 256;
const SAIL_CLOTH = '#fbf8f1';
const SAIL_TIMBER = '#9a6a45';
const SAIL_FRAME_PX = 5;
const SAIL_SLAT_PX = 3;
const SAIL_SLAT_STEP = 32;

const BALL_W = 128;
const BALL_H = 64;
const BALL_DIMPLES = 90;
const BALL_DIMPLE_R = 1.6;
const BALL_SEAM_PX = 4;
const BALL_SEAM = 'rgba(0,0,0,0.22)';

export type TextureSet = {
  grass: Texture;
  stone: Texture;
  wood: Texture;
  background: Texture;
};

const cache = new Map<number, TextureSet>();

// Seeded so every client renders the same speckles for a palette.
function rng(seed: number) {
  return () => ((seed = (seed * 16807) % 2147483647) - 1) / 2147483646;
}

export function canvas(
  w: number,
  h: number,
  draw: (g: CanvasRenderingContext2D, r: () => number) => void,
  wrap = true
) {
  const c = document.createElement('canvas');
  c.width = w;
  c.height = h;
  draw(c.getContext('2d')!, rng(w * 31 + h * 7 + 1));
  const tex = new CanvasTexture(c);
  tex.colorSpace = SRGBColorSpace;
  if (wrap) tex.wrapS = tex.wrapT = RepeatWrapping;
  tex.anisotropy = ANISOTROPY;
  return tex;
}

function speckle(g: CanvasRenderingContext2D, r: () => number, n: number, alpha: number) {
  for (let i = 0; i < n; i++) {
    g.fillStyle = r() > 0.5 ? `rgba(255,255,255,${alpha * r()})` : `rgba(0,0,0,${alpha * r()})`;
    g.fillRect(r() * g.canvas.width, r() * g.canvas.height, SPECKLE_PX, SPECKLE_PX);
  }
}

/** Two-tone mowing stripes. */
function paintGrass(p: Palette) {
  return canvas(GRASS_PX, GRASS_PX, (g, r) => {
    g.fillStyle = p.grass;
    g.fillRect(0, 0, GRASS_PX, GRASS_PX);
    g.fillStyle = p.stripe;
    g.fillRect(0, 0, GRASS_PX / 2, GRASS_PX);
    speckle(g, r, GRASS_SPECKLES, GRASS_SPECKLE_ALPHA);
  });
}

/** Running-bond bricks. */
function paintStone(p: Palette) {
  return canvas(STONE_PX, STONE_PX, (g, r) => {
    g.fillStyle = p.stone;
    g.fillRect(0, 0, STONE_PX, STONE_PX);
    speckle(g, r, STONE_SPECKLES, STONE_SPECKLE_ALPHA);
    g.strokeStyle = MORTAR;
    g.lineWidth = MORTAR_PX;
    for (let row = 0; row < BRICK_ROWS; row++) {
      const y = row * BRICK_H;
      // Wider than the tile so only the row's horizontal joints show; the vertical ones come from the loop below.
      g.strokeRect(-2, y, STONE_PX + 4, BRICK_H);
      for (let x = row % 2 ? 0 : BRICK_W / 2; x < STONE_PX; x += BRICK_W) {
        g.beginPath();
        g.moveTo(x, y);
        g.lineTo(x, y + BRICK_H);
        g.stroke();
      }
    }
  });
}

/** End grain: concentric growth rings. */
function paintWood(p: Palette) {
  return canvas(WOOD_PX, WOOD_PX, (g) => {
    g.fillStyle = p.wood;
    g.fillRect(0, 0, WOOD_PX, WOOD_PX);
    for (let rad = WOOD_RING_OUTER; rad > WOOD_RING_INNER; rad -= WOOD_RING_STEP) {
      // Alternating ring darkness, like early/late wood.
      g.strokeStyle = `rgba(0,0,0,${0.12 + (rad % 14) / 70})`;
      g.lineWidth = WOOD_RING_PX;
      g.beginPath();
      g.arc(WOOD_PX / 2, WOOD_PX / 2, rad, 0, Math.PI * 2);
      g.stroke();
    }
  });
}

function paintBackground(p: Palette) {
  return canvas(
    BACKGROUND_W,
    BACKGROUND_H,
    (g) => {
      const grad = g.createLinearGradient(0, 0, 0, BACKGROUND_H);
      grad.addColorStop(0, p.bgTop);
      grad.addColorStop(1, p.bgBottom);
      g.fillStyle = grad;
      g.fillRect(0, 0, BACKGROUND_W, BACKGROUND_H);
    },
    false
  );
}

export function texturesFor(palette: number): TextureSet {
  const key = paletteIndex(palette);
  let set = cache.get(key);
  if (!set) {
    const p = paletteOf(key);
    set = {grass: paintGrass(p), stone: paintStone(p), wood: paintWood(p), background: paintBackground(p)};
    cache.set(key, set);
  }
  return set;
}

let chevronTex: Texture | null = null;

/** One white chevron per tile pointing toward +u; slope cells map +u downhill. */
export function chevronTexture() {
  return (chevronTex ??= canvas(
    CHEVRON_PX,
    CHEVRON_PX,
    (g) => {
      g.strokeStyle = '#ffffff';
      g.lineWidth = CHEVRON_STROKE;
      g.lineCap = g.lineJoin = 'round';
      g.beginPath();
      g.moveTo(CHEVRON_ARM_X, CHEVRON_ARM_INSET);
      g.lineTo(CHEVRON_TIP_X, CHEVRON_PX / 2);
      g.lineTo(CHEVRON_ARM_X, CHEVRON_PX - CHEVRON_ARM_INSET);
      g.stroke();
    },
    false
  ));
}

let sailTex: Texture | null = null;

/** Canvas sail on a timber lattice, like a classic windmill blade. */
export function sailTexture() {
  return (sailTex ??= canvas(
    SAIL_W,
    SAIL_H,
    (g) => {
      g.fillStyle = SAIL_CLOTH;
      g.fillRect(0, 0, SAIL_W, SAIL_H);
      g.strokeStyle = SAIL_TIMBER;
      g.lineWidth = SAIL_FRAME_PX;
      // Inset by half the line width so the whole frame stays on the canvas.
      const inset = SAIL_FRAME_PX / 2;
      g.strokeRect(inset, inset, SAIL_W - SAIL_FRAME_PX, SAIL_H - SAIL_FRAME_PX);
      g.lineWidth = SAIL_SLAT_PX;
      for (let y = SAIL_SLAT_STEP; y < SAIL_H; y += SAIL_SLAT_STEP) {
        g.beginPath();
        g.moveTo(0, y);
        g.lineTo(SAIL_W, y);
        g.stroke();
      }
      g.beginPath();
      g.moveTo(SAIL_W / 2, 0);
      g.lineTo(SAIL_W / 2, SAIL_H);
      g.stroke();
    },
    false
  ));
}

let ballTex: Texture | null = null;

/** White ball with a seam and dimples so the roll rotation is visible under per-instance tint. */
export function ballTexture() {
  return (ballTex ??= canvas(BALL_W, BALL_H, (g, r) => {
    g.fillStyle = '#ffffff';
    g.fillRect(0, 0, BALL_W, BALL_H);
    for (let i = 0; i < BALL_DIMPLES; i++) {
      g.fillStyle = `rgba(0,0,0,${0.05 + r() * 0.06})`;
      g.beginPath();
      g.arc(r() * BALL_W, r() * BALL_H, BALL_DIMPLE_R, 0, Math.PI * 2);
      g.fill();
    }
    g.fillStyle = BALL_SEAM;
    g.fillRect(0, (BALL_H - BALL_SEAM_PX) / 2, BALL_W, BALL_SEAM_PX);
  }));
}
