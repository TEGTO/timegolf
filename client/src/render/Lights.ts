import {DirectionalLight, HemisphereLight, Vector3, type Scene} from 'three';
import {SUN_DIR} from './constants';
import type {LevelView} from './LevelMesh';
import {paletteOf, type Palette} from './palettes';

const HEMI_INTENSITY = 1.35;
const SUN_COLOR = '#fff0d4';
const SUN_INTENSITY = 2.3;
const SHADOW_MAP_SIZE = 4096;
// Casters render back faces into the map, so lit faces never self-shadow; any real bias detaches shadows from their objects.
const SHADOW_BIAS = -0.00002;
const SHADOW_RADIUS = 2;
/** Sun distance from the level center, in level radii: outside everything it lights. */
const SUN_DISTANCE = 2;
/** Depth slack around the level box so nothing at its faces is clipped out of the shadow map. */
const SHADOW_DEPTH_PAD = 0.5;
const BOX_CORNERS = 8;

/** Hemisphere sky light plus a shadow-casting sun whose shadow frustum hugs the level. */
export class Lights {
  private readonly hemi: HemisphereLight;
  private readonly sun = new DirectionalLight(SUN_COLOR, SUN_INTENSITY);
  private readonly probe = new Vector3();

  constructor(scene: Scene, maxTextureSize: number) {
    const initial = paletteOf(0);
    this.hemi = new HemisphereLight(initial.sky, initial.ground, HEMI_INTENSITY);
    this.sun.castShadow = true;
    const size = Math.min(SHADOW_MAP_SIZE, maxTextureSize);
    this.sun.shadow.mapSize.set(size, size);
    this.sun.shadow.bias = SHADOW_BIAS;
    this.sun.shadow.normalBias = 0;
    this.sun.shadow.radius = SHADOW_RADIUS;
    scene.add(this.hemi, this.sun, this.sun.target);
  }

  setPalette(pal: Palette) {
    this.hemi.color.set(pal.sky);
    this.hemi.groundColor.set(pal.ground);
  }

  /** Shadow frustum hugging the level box in light space: smallest texels, tightest depth range. */
  fit(view: LevelView) {
    const {min, max} = view.bounds;
    this.sun.target.position.copy(view.center);
    this.sun.position.copy(view.center).addScaledVector(SUN_DIR, view.radius * SUN_DISTANCE);
    this.sun.updateMatrixWorld();
    this.sun.target.updateMatrixWorld();
    this.sun.shadow.updateMatrices(this.sun);
    const cam = this.sun.shadow.camera;
    let x0 = Infinity;
    let x1 = -Infinity;
    let y0 = Infinity;
    let y1 = -Infinity;
    let z0 = Infinity;
    let z1 = -Infinity;
    for (let k = 0; k < BOX_CORNERS; k++) {
      const v = this.probe
        .set(k & 1 ? max.x : min.x, k & 2 ? max.y : min.y, k & 4 ? max.z : min.z)
        .applyMatrix4(cam.matrixWorldInverse);
      x0 = Math.min(x0, v.x);
      x1 = Math.max(x1, v.x);
      y0 = Math.min(y0, v.y);
      y1 = Math.max(y1, v.y);
      z0 = Math.min(z0, v.z);
      z1 = Math.max(z1, v.z);
    }
    cam.left = x0;
    cam.right = x1;
    cam.bottom = y0;
    cam.top = y1;
    cam.near = -z1 - SHADOW_DEPTH_PAD;
    cam.far = -z0 + SHADOW_DEPTH_PAD;
    cam.updateProjectionMatrix();
  }
}
