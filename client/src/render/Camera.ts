import {OrthographicCamera, Plane, Raycaster, Vector2, Vector3, type Ray} from 'three';
import type {ScreenPoint} from '../input';
import {UP} from './constants';

/** World units visible top to bottom at zoom 1. */
const VIEW_UNITS = 16;
// Orthographic, so distance doesn't change the picture; it only has to keep the course in front of the near plane.
const CAMERA_DISTANCE = 60;
/** Target-to-camera direction at yaw 0. */
const CAMERA_OFFSET = new Vector3(8, 12, 10).normalize().multiplyScalar(CAMERA_DISTANCE);
const NEAR = 0.1;
const FAR = 400;
const MIN_ZOOM = 0.5;
const MAX_ZOOM = 2.5;
const ZOOM_PER_WHEEL = 0.0012;
const ROTATE_PER_PX = 0.008;
const QUARTER_TURN = Math.PI / 2;
/** Exponential easing per ms: target toward the followed ball, yaw toward its goal. */
const FOLLOW_RATE = 0.004;
const ROTATE_RATE = 0.012;

/** Orthographic camera orbiting `target` by yaw, with zoom, pan and screen ↔ world picking. */
export class CameraRig {
  readonly camera = new OrthographicCamera(-1, 1, 1, -1, NEAR, FAR);
  readonly target = new Vector3();
  /** Target chases the followed ball; a pan turns this off until refocus. */
  follow = true;
  private zoom = 1;
  private yaw = 0;
  private yawTarget = 0;
  private height = 1;
  private readonly offset = new Vector3();
  private readonly raycaster = new Raycaster();
  private readonly ndc = new Vector2();
  private readonly ground = new Plane(UP.clone(), 0);
  private readonly panA = new Vector3();
  private readonly panB = new Vector3();
  private readonly probe = new Vector3();

  constructor(private readonly canvas: HTMLElement) {}

  resize(width: number, height: number) {
    this.height = height;
    const aspect = width / height;
    this.camera.left = (-VIEW_UNITS * aspect) / 2;
    this.camera.right = (VIEW_UNITS * aspect) / 2;
    this.camera.top = VIEW_UNITS / 2;
    this.camera.bottom = -VIEW_UNITS / 2;
    this.camera.updateProjectionMatrix();
  }

  /** Quarter turns around the target, eased in `update`. */
  rotate(steps: number) {
    this.yawTarget += steps * QUARTER_TURN;
  }

  /** Free rotation by a horizontal drag, applied at once. */
  rotateBy(dxPx: number) {
    this.yaw = this.yawTarget -= dxPx * ROTATE_PER_PX;
  }

  zoomBy(wheelDeltaY: number) {
    this.zoom = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, this.zoom * Math.exp(-wheelDeltaY * ZOOM_PER_WHEEL)));
    this.camera.zoom = this.zoom;
    this.camera.updateProjectionMatrix();
  }

  /** Drags the ground point under `from` to `to`. */
  pan(from: ScreenPoint, to: ScreenPoint) {
    this.ground.constant = -this.target.y;
    const a = this.ray(from).intersectPlane(this.ground, this.panA);
    const b = a && this.ray(to).intersectPlane(this.ground, this.panB);
    if (!a || !b) return;
    this.target.add(this.panA.sub(this.panB));
    this.follow = false;
  }

  ray(at: ScreenPoint): Ray {
    const r = this.canvas.getBoundingClientRect();
    this.ndc.set(((at.clientX - r.left) / r.width) * 2 - 1, -((at.clientY - r.top) / r.height) * 2 + 1);
    this.raycaster.setFromCamera(this.ndc, this.camera);
    return this.raycaster.ray;
  }

  worldPerPixel() {
    return VIEW_UNITS / this.zoom / this.height;
  }

  /** Screen px between `at` and the projection of `p`. */
  screenDistance(at: ScreenPoint, p: Vector3) {
    const r = this.canvas.getBoundingClientRect();
    this.probe.copy(p).project(this.camera);
    const x = r.left + ((this.probe.x + 1) / 2) * r.width;
    const y = r.top + ((1 - this.probe.y) / 2) * r.height;
    return Math.hypot(at.clientX - x, at.clientY - y);
  }

  /** Per frame: eases toward `chase` (when following and there is a ball) and the yaw goal, then places the camera. */
  update(dtMs: number, chase: Vector3 | null) {
    if (chase) this.target.lerp(chase, 1 - Math.exp(-dtMs * FOLLOW_RATE));
    this.yaw += (this.yawTarget - this.yaw) * (1 - Math.exp(-dtMs * ROTATE_RATE));
    this.offset.copy(CAMERA_OFFSET).applyAxisAngle(UP, this.yaw);
    this.camera.position.copy(this.target).add(this.offset);
    this.camera.lookAt(this.target);
  }
}
