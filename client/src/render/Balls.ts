import {
  Color,
  DynamicDrawUsage,
  InstancedMesh,
  Matrix4,
  Mesh,
  MeshBasicMaterial,
  MeshStandardMaterial,
  Quaternion,
  RingGeometry,
  SphereGeometry,
  Vector3,
  type Object3D
} from 'three';
import {world} from '../playback';
import type {PlayerDto} from '../types';
import type {LevelView} from './LevelMesh';
import {ballTexture} from './textures';

const CAPACITY = 64;
const FALLBACK_COLOR = '#dddddd';
const WIDTH_SEGMENTS = 20;
const HEIGHT_SEGMENTS = 14;
const ROUGHNESS = 0.35;
const METALNESS = 0.05;
/** Own-ball ring, in ball radii. */
const RING_INNER = 1.55;
const RING_OUTER = 1.95;
const RING_SEGMENTS = 40;
const RING_OPACITY = 0.8;
const RING_LIFT = 0.01;
// Inactive players fade out of the way: less saturated, a touch lighter.
const INACTIVE_SATURATION = -0.45;
const INACTIVE_LIGHTNESS = 0.05;
/** Current-turn ball: extra scale at the pulse peak, and the pulse's time scale. */
const PULSE_SCALE = 0.12;
const PULSE_MS = 160;
// Sinking: slide to the cup center in the first half, shrinking and dropping below the green.
const SINK_SLIDE = 2;
const SINK_SHRINK = 0.5;
const SINK_DEPTH_RADII = 2.4;
const MIN_ROLL = 1e-5;
// Big jumps are checkpoint switches, not rolling.
const MAX_ROLL = 1;

type Spin = {quat: Quaternion; x: number; z: number};

/** One ball per player, drawn from the playback world. */
export class Balls {
  private readonly mesh: InstancedMesh;
  private readonly geometry: SphereGeometry;
  private readonly material: MeshStandardMaterial;
  private readonly ring: Mesh<RingGeometry, MeshBasicMaterial>;
  private readonly colors = new Map<string, Color>();
  private readonly spins = new Map<string, Spin>();
  private readonly fallback = new Color(FALLBACK_COLOR);
  private readonly m = new Matrix4();
  private readonly pos = new Vector3();
  private readonly scale = new Vector3();
  private readonly axis = new Vector3();
  private readonly spin = new Quaternion();

  constructor(
    private readonly parent: Object3D,
    private readonly radius: number
  ) {
    this.geometry = new SphereGeometry(radius, WIDTH_SEGMENTS, HEIGHT_SEGMENTS);
    this.material = new MeshStandardMaterial({map: ballTexture(), roughness: ROUGHNESS, metalness: METALNESS});
    this.mesh = new InstancedMesh(this.geometry, this.material, CAPACITY);
    this.mesh.instanceMatrix.setUsage(DynamicDrawUsage);
    this.mesh.setColorAt(0, new Color());
    this.mesh.castShadow = true;
    this.mesh.count = 0;
    this.mesh.frustumCulled = false;
    this.ring = new Mesh(
      new RingGeometry(radius * RING_INNER, radius * RING_OUTER, RING_SEGMENTS),
      new MeshBasicMaterial({color: '#ffffff', transparent: true, opacity: RING_OPACITY, depthWrite: false})
    );
    this.ring.rotation.x = -Math.PI / 2;
    this.ring.visible = false;
    parent.add(this.mesh, this.ring);
  }

  syncColors(players: PlayerDto[]) {
    for (const p of players) {
      let c = this.colors.get(p.id);
      if (!c) this.colors.set(p.id, (c = new Color()));
      c.setStyle(p.color);
      if (!p.active) c.offsetHSL(0, INACTIVE_SATURATION, INACTIVE_LIGHTNESS);
    }
  }

  /** Writes the ball's rendered position into `out`; false when it has no visible ball. */
  position(id: string | null, view: LevelView, out: Vector3) {
    for (let i = 0; i < world.length; i++) {
      const b = world[i];
      if (b.playerId !== id) continue;
      if (b.sink > 0) return false;
      out.set(b.x, view.heightAt(b.x, b.z) + this.radius, b.z);
      return true;
    }
    return false;
  }

  update(view: LevelView, me: string | null, turnId: string | null, now: number) {
    let n = 0;
    this.ring.visible = false;
    for (let i = 0; i < world.length && n < CAPACITY; i++) {
      const b = world[i];
      if (b.sink >= 1) continue;
      let x = b.x;
      let z = b.z;
      let s = 1;
      let drop = 0;
      if (b.sink > 0) {
        const k = Math.min(1, b.sink * SINK_SLIDE);
        x += (view.hole.x - x) * k;
        z += (view.hole.z - z) * k;
        s = 1 - b.sink * SINK_SHRINK;
        drop = b.sink * this.radius * SINK_DEPTH_RADII;
      } else if (b.playerId === turnId) s = 1 + PULSE_SCALE * (0.5 + 0.5 * Math.sin(now / PULSE_MS));
      this.pos.set(x, view.heightAt(x, z) + this.radius - drop, z);
      this.mesh.setMatrixAt(n, this.m.compose(this.pos, this.roll(b.playerId, x, z), this.scale.setScalar(s)));
      this.mesh.setColorAt(n, this.colors.get(b.playerId) ?? this.fallback);
      n++;
      if (b.playerId === me && b.sink === 0) {
        this.ring.visible = true;
        this.ring.position.set(x, this.pos.y - this.radius + RING_LIFT, z);
        this.ring.material.color.copy(this.colors.get(me) ?? this.fallback);
      }
    }
    this.mesh.count = n;
    this.mesh.instanceMatrix.needsUpdate = true;
    if (this.mesh.instanceColor) this.mesh.instanceColor.needsUpdate = true;
  }

  private roll(id: string, x: number, z: number) {
    let r = this.spins.get(id);
    if (!r) this.spins.set(id, (r = {quat: new Quaternion(), x, z}));
    const dx = x - r.x;
    const dz = z - r.z;
    const dist = Math.hypot(dx, dz);
    r.x = x;
    r.z = z;
    if (dist > MIN_ROLL && dist < MAX_ROLL) {
      this.spin.setFromAxisAngle(this.axis.set(dz / dist, 0, -dx / dist), dist / this.radius);
      r.quat.premultiply(this.spin);
    }
    return r.quat;
  }

  dispose() {
    this.parent.remove(this.mesh, this.ring);
    this.mesh.dispose();
    this.geometry.dispose();
    this.material.dispose();
    this.ring.geometry.dispose();
    this.ring.material.dispose();
  }
}
