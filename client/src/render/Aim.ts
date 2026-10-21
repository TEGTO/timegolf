import {
  BufferAttribute,
  BufferGeometry,
  Group,
  Mesh,
  MeshBasicMaterial,
  Plane,
  PlaneGeometry,
  Vector3,
  type Object3D,
  type Ray
} from 'three';
import {RENDER_ORDER, UP} from './constants';

/** Drag length (world units) for full power. */
const POWER_UNITS = 3;
export const MIN_POWER = 0.03;
const MIN_DRAG = 1e-6;
const SHAFT_W = 0.07;
const HEAD_HALF_W = 0.14;
const HEAD_LEN = 0.26;
const ARROW_MIN_LEN = 0.3;
const ARROW_LEN_PER_POWER = 2.2;
const AIM_OPACITY = 0.85;
const ORIGINAL_OPACITY = 0.35;
/** Arrow height above the ground. */
export const ARROW_HEIGHT = 0.04;
// The live arrow floats a hair above the faint original so the two never z-fight.
const AIM_ABOVE_ORIGINAL = 0.005;
// Arrow color runs from green (hue 0.33) at no power to red at full power.
const WEAK_HUE = 0.33;
const ARROW_SATURATION = 0.9;
const ARROW_LIGHTNESS = 0.55;

export class Arrow {
  readonly group = new Group();
  readonly material: MeshBasicMaterial;
  private readonly shaft: Mesh;
  private readonly head: Mesh;

  constructor(opacity: number) {
    this.material = new MeshBasicMaterial({color: '#ffffff', transparent: true, opacity, depthWrite: false});
    this.shaft = new Mesh(new PlaneGeometry(1, SHAFT_W).rotateX(-Math.PI / 2).translate(0.5, 0, 0), this.material);
    const headGeo = new BufferGeometry();
    headGeo.setAttribute(
      'position',
      new BufferAttribute(new Float32Array([0, 0, -HEAD_HALF_W, HEAD_LEN, 0, 0, 0, 0, HEAD_HALF_W]), 3)
    );
    this.head = new Mesh(headGeo, this.material);
    this.group.add(this.shaft, this.head);
    this.group.visible = false;
    this.group.renderOrder = RENDER_ORDER.overlay;
  }

  place(origin: Vector3, lift: number, dirX: number, dirZ: number, power: number) {
    const len = ARROW_MIN_LEN + power * ARROW_LEN_PER_POWER;
    this.group.position.set(origin.x, origin.y - lift, origin.z);
    this.group.rotation.y = -Math.atan2(dirZ, dirX);
    this.shaft.scale.x = len;
    this.head.position.x = len;
  }

  dispose() {
    this.shaft.geometry.dispose();
    this.head.geometry.dispose();
    this.material.dispose();
  }
}

/** Drag-back slingshot from `origin`, plus a faint arrow of the shot being re-done; `onAim` reports the live aim (power null = stopped). */
export class Aim {
  readonly origin = new Vector3();
  canAim = false;
  private aiming = false;
  private power = 0;
  private readonly dir = new Vector3();
  private readonly hit = new Vector3();
  private readonly plane = new Plane(UP.clone(), 0);
  private readonly arrow = new Arrow(AIM_OPACITY);
  private readonly original = new Arrow(ORIGINAL_OPACITY);
  private shown: {dirX: number; dirZ: number; power: number} | null = null;

  constructor(
    private readonly parent: Object3D,
    private readonly radius: number,
    private readonly onShoot: (dirX: number, dirZ: number, power: number) => void,
    private readonly onAim: (dirX: number, dirZ: number, power: number | null) => void
  ) {
    parent.add(this.arrow.group, this.original.group);
  }

  get isAiming() {
    return this.aiming;
  }

  setOriginal(shot: {dirX: number; dirZ: number; power: number} | null) {
    this.shown = shot;
  }

  private ground(ray: Ray) {
    this.plane.constant = -this.origin.y;
    return ray.intersectPlane(this.plane, this.hit);
  }

  start() {
    if (!this.canAim) return false;
    this.aiming = true;
    this.power = 0;
    return true;
  }

  drag(ray: Ray) {
    if (!this.aiming || !this.ground(ray)) return;
    this.dir.set(this.origin.x - this.hit.x, 0, this.origin.z - this.hit.z);
    const len = this.dir.length();
    this.power = Math.min(1, len / POWER_UNITS);
    if (len > MIN_DRAG) this.dir.divideScalar(len);
    this.onAim(this.dir.x, this.dir.z, this.power);
  }

  private stop() {
    this.aiming = false;
    this.onAim(this.dir.x, this.dir.z, null);
  }

  /** `fire` is false for a click without a real drag, which must never shoot. */
  release(fire: boolean) {
    if (!this.aiming) return;
    this.stop();
    if (fire && this.canAim && this.power > MIN_POWER) this.onShoot(this.dir.x, this.dir.z, this.power);
  }

  cancel() {
    if (!this.aiming) return false;
    this.stop();
    return true;
  }

  /** Per frame: keeps the arrows glued to the origin. */
  update() {
    if (!this.canAim && this.aiming) this.stop();
    const lift = this.radius - ARROW_HEIGHT;
    this.original.group.visible = this.canAim && this.shown !== null;
    if (this.shown) this.original.place(this.origin, lift, this.shown.dirX, this.shown.dirZ, this.shown.power);
    this.arrow.group.visible = this.aiming && this.power > MIN_POWER;
    if (!this.arrow.group.visible) return;
    this.arrow.place(this.origin, lift - AIM_ABOVE_ORIGINAL, this.dir.x, this.dir.z, this.power);
    this.arrow.material.color.setHSL((1 - this.power) * WEAK_HUE, ARROW_SATURATION, ARROW_LIGHTNESS);
  }

  dispose() {
    this.parent.remove(this.arrow.group, this.original.group);
    this.arrow.dispose();
    this.original.dispose();
  }
}
