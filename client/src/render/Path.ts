import {
  BufferAttribute,
  BufferGeometry,
  DoubleSide,
  Mesh,
  MeshBasicMaterial,
  RingGeometry,
  Vector3,
  type Camera,
  type Object3D
} from 'three';
import {UNKNOWN_PLAYER_COLOR} from '../constants';
import {trackOf, type Changed} from '../store';
import type {HitPlayDto, PlayerDto} from '../types';
import {PathBadges} from './Badges';
import {RENDER_ORDER} from './constants';
import type {LevelView} from './LevelMesh';

const HALF_WIDTH = 0.06;
const GHOST_HALF_WIDTH = 0.045;
/** Quads are at most this long so the ribbon follows ramps and tier edges. */
const PIECE = 0.12;
const DASH = 0.22;
const LIFT = 0.02;
const GHOST_LIFT = 0.012;
const OPACITY = 0.8;
const DIM_OPACITY = 0.15;
const GHOST_COLOR = '#5f6b7a';
const GHOST_OPACITY = 0.6;
const END_OPACITY = 0.7;
const END_INNER = 0.1;
const END_OUTER = 0.16;
const END_SEGMENTS = 28;
const MIN_STEP = 1e-4;
// Badge sits a little way into its segment so the badges of back-to-back segments don't share one spot.
const BADGE_ALONG = 0.35;

type Ribbon = Mesh<BufferGeometry, MeshBasicMaterial>;
type Segment = {index: number; mesh: Ribbon};

/**
 * My ball's future from the viewed checkpoint: one ribbon per record that moved it, colored by the hitter
 * (solid = my hit, dashed = knocked), `#n` badges, rest ring; plus the grey ghost of the path a replace overwrote.
 */
export class FuturePath {
  private segments: Segment[] = [];
  private readonly badges: PathBadges;
  private readonly ghost = ribbon(GHOST_OPACITY, RENDER_ORDER.ghost);
  private readonly end = ring(END_OPACITY);
  private readonly ghostEnd = ring(GHOST_OPACITY);

  constructor(
    private readonly parent: Object3D,
    host: HTMLElement
  ) {
    this.badges = new PathBadges(host);
    this.ghost.material.color.setStyle(GHOST_COLOR);
    this.ghostEnd.material.color.setStyle(GHOST_COLOR);
    parent.add(this.ghost, this.end, this.ghostEnd);
  }

  /** Rebuilt only when the viewed checkpoint, timeline or players change, never per frame. */
  show(
    view: LevelView | null,
    timeline: HitPlayDto[],
    from: number | null,
    me: string | null,
    players: PlayerDto[],
    changed: Changed | null
  ) {
    this.clear();
    if (!view || from === null || !me) return;
    const player = (id: string) => players.find((p) => p.id === id);
    const colorOf = (id: string) => player(id)?.color ?? UNKNOWN_PLAYER_COLOR;
    const by = changed && `#${changed.index + 1} · ${player(changed.playerId)?.name ?? '?'}`;
    let merged = false;

    for (let i = from; i < timeline.length; i++) {
      const hit = timeline[i];
      const p = trackOf(hit, me);
      if (!p) continue;
      const positions: number[] = [];
      stroke(view, p, positions, hit.playerId === me ? 0 : DASH, HALF_WIDTH, LIFT);
      const mesh = ribbon(OPACITY, RENDER_ORDER.overlay);
      fill(mesh, positions);
      mesh.material.color.setStyle(colorOf(hit.playerId));
      this.parent.add(mesh);
      this.segments.push({index: i, mesh});
      // The replaced hit itself moved my ball differently: one badge says both "#n" and who changed it.
      const cause = changed && i === changed.at && i === changed.index;
      if (cause) merged = true;
      this.badge(view, i, along(p), cause ? by! : `#${i + 1}`, colorOf(hit.playerId), !!cause);
      place(this.end, view, p, LIFT);
    }
    this.end.material.color.setStyle(colorOf(me));

    if (changed) {
      const positions: number[] = [];
      for (const hit of changed.previous) {
        const p = trackOf(hit, me);
        if (!p) continue;
        stroke(view, p, positions, 0, GHOST_HALF_WIDTH, GHOST_LIFT);
        place(this.ghostEnd, view, p, GHOST_LIFT);
      }
      fill(this.ghost, positions);
      const start = timeline[changed.at]?.start.find((b) => b.playerId === me);
      if (!merged && start) this.badge(view, changed.index, [start.x, start.z], by!, colorOf(changed.playerId), true);
    }
    this.badges.measure();
  }

  /** Hovered timeline marker: its segments and badges stand out, the rest fade; no-op when that hit has neither. */
  highlight(index: number | null) {
    const hot = index !== null && this.badges.has(index);
    for (const s of this.segments) {
      const on = !hot || s.index === index;
      s.mesh.material.opacity = on ? OPACITY : DIM_OPACITY;
      s.mesh.renderOrder = hot && on ? RENDER_ORDER.highlight : RENDER_ORDER.overlay;
    }
    this.badges.focus(hot ? index : null);
  }

  /** Per frame after render. */
  update(camera: Camera, width: number, height: number) {
    this.badges.update(camera, width, height);
  }

  private badge(view: LevelView, index: number, [x, z]: [number, number], text: string, color: string, cause: boolean) {
    this.badges.add(index, new Vector3(x, view.heightAt(x, z), z), text, color, cause);
  }

  private clear() {
    for (const s of this.segments) {
      this.parent.remove(s.mesh);
      s.mesh.geometry.dispose();
      s.mesh.material.dispose();
    }
    this.segments = [];
    this.badges.clear();
    fill(this.ghost, []);
    this.end.visible = false;
    this.ghostEnd.visible = false;
  }

  dispose() {
    this.clear();
    this.badges.dispose();
    this.parent.remove(this.ghost, this.end, this.ghostEnd);
    for (const m of [this.ghost, this.end, this.ghostEnd]) {
      m.geometry.dispose();
      m.material.dispose();
    }
  }
}

function ribbon(opacity: number, renderOrder: number): Ribbon {
  const mesh = new Mesh(
    new BufferGeometry(),
    new MeshBasicMaterial({transparent: true, opacity, depthWrite: false, side: DoubleSide})
  );
  mesh.visible = false;
  mesh.renderOrder = renderOrder;
  return mesh;
}

function ring(opacity: number) {
  const mesh = new Mesh(
    new RingGeometry(END_INNER, END_OUTER, END_SEGMENTS).rotateX(-Math.PI / 2),
    new MeshBasicMaterial({transparent: true, opacity, depthWrite: false})
  );
  mesh.visible = false;
  mesh.renderOrder = RENDER_ORDER.overlay;
  return mesh;
}

/** Moves the ring to the polyline's last point (rest or hole). */
function place(mesh: Mesh, view: LevelView, p: number[], lift: number) {
  const x = p[p.length - 2];
  const z = p[p.length - 1];
  mesh.position.set(x, view.heightAt(x, z) + lift, z);
  mesh.visible = true;
}

/** Point `BADGE_ALONG` into the polyline, or its midpoint when shorter than twice that. */
function along(p: number[]): [number, number] {
  let total = 0;
  for (let k = 0; k + 3 < p.length; k += 2) total += Math.hypot(p[k + 2] - p[k], p[k + 3] - p[k + 1]);
  let left = Math.min(BADGE_ALONG, total / 2);
  for (let k = 0; k + 3 < p.length; k += 2) {
    const len = Math.hypot(p[k + 2] - p[k], p[k + 3] - p[k + 1]);
    if (len >= left && len > 0) {
      const f = left / len;
      return [p[k] + (p[k + 2] - p[k]) * f, p[k + 1] + (p[k + 3] - p[k + 1]) * f];
    }
    left -= len;
  }
  return [p[0], p[1]];
}

/** Appends quads along the polyline; `dash` > 0 leaves every other `dash`-long stretch out. */
function stroke(view: LevelView, p: number[], out: number[], dash: number, halfWidth: number, lift: number) {
  let dist = 0;
  for (let k = 0; k + 3 < p.length; k += 2) {
    const ax = p[k];
    const az = p[k + 1];
    const len = Math.hypot(p[k + 2] - ax, p[k + 3] - az);
    if (len < MIN_STEP) continue;
    const ux = (p[k + 2] - ax) / len;
    const uz = (p[k + 3] - az) / len;
    const n = Math.ceil(len / PIECE);
    for (let j = 0; j < n; j++) {
      const s0 = (len * j) / n;
      const s1 = (len * (j + 1)) / n;
      if (dash > 0 && Math.floor((dist + (s0 + s1) / 2) / dash) % 2 === 1) continue;
      quad(view, out, lift, ax + ux * s0, az + uz * s0, ax + ux * s1, az + uz * s1, -uz * halfWidth, ux * halfWidth);
    }
    dist += len;
  }
}

function quad(
  view: LevelView,
  out: number[],
  lift: number,
  x0: number,
  z0: number,
  x1: number,
  z1: number,
  nx: number,
  nz: number
) {
  const y0 = view.heightAt(x0, z0) + lift;
  const y1 = view.heightAt(x1, z1) + lift;
  out.push(x0 + nx, y0, z0 + nz, x0 - nx, y0, z0 - nz, x1 + nx, y1, z1 + nz);
  out.push(x1 + nx, y1, z1 + nz, x0 - nx, y0, z0 - nz, x1 - nx, y1, z1 - nz);
}

function fill(mesh: Ribbon, positions: number[]) {
  mesh.visible = positions.length > 0;
  // A fresh geometry (not a swapped attribute) so three.js frees the old GPU buffer.
  mesh.geometry.dispose();
  mesh.geometry = new BufferGeometry().setAttribute('position', new BufferAttribute(new Float32Array(positions), 3));
}
