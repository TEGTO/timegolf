import {
  BoxGeometry,
  CylinderGeometry,
  InstancedMesh,
  Matrix4,
  MeshStandardMaterial,
  Quaternion,
  Vector3,
  type Group
} from 'three';
import type {LevelDto} from '../types';
import {STONE_ROUGHNESS, UP, WOOD_ROUGHNESS} from './constants';
import type {TextureSet} from './textures';

const WALL_H = 0.25;
const WALL_T = 0.12;
// Each wall piece samples the floor this far out on both sides to find the tier it borders.
const WALL_PROBE = 0.05;
const PILLAR_H = 0.6;
const BUMPER_H = 0.3;
const BUMPER_SEGMENTS = 28;
const EPSILON = 1e-6;

/** Walls, pillars and bumpers as instanced meshes, added to `group`. */
export function buildObstacles(
  level: LevelDto,
  heightAt: (x: number, z: number) => number,
  tex: TextureSet,
  group: Group
) {
  const m = new Matrix4();
  const q = new Quaternion();
  const p = new Vector3();
  const s = new Vector3();
  const stoneMat = new MeshStandardMaterial({map: tex.stone, roughness: STONE_ROUGHNESS});

  // Walls are split into ≤1-unit pieces so each piece can sit on the tier it borders.
  const pieces: [number, number, number, number, number][] = [];
  for (const [x1, z1, x2, z2] of level.walls) {
    const len = Math.hypot(x2 - x1, z2 - z1);
    if (len < EPSILON) continue;
    const n = Math.ceil(len - EPSILON);
    const nx = ((z1 - z2) / len) * WALL_PROBE;
    const nz = ((x2 - x1) / len) * WALL_PROBE;
    for (let k = 0; k < n; k++) {
      const mx = x1 + ((x2 - x1) * (k + 0.5)) / n;
      const mz = z1 + ((z2 - z1) * (k + 0.5)) / n;
      const y = Math.max(heightAt(mx + nx, mz + nz), heightAt(mx - nx, mz - nz));
      pieces.push([mx, y, mz, len / n, Math.atan2(z2 - z1, x2 - x1)]);
    }
  }
  if (pieces.length > 0) {
    const walls = new InstancedMesh(new BoxGeometry(1, WALL_H, WALL_T), stoneMat, pieces.length);
    pieces.forEach(([x, y, z, len, angle], i) => {
      q.setFromAxisAngle(UP, -angle);
      walls.setMatrixAt(i, m.compose(p.set(x, y + WALL_H / 2, z), q, s.set(len + WALL_T, 1, 1)));
    });
    walls.castShadow = walls.receiveShadow = true;
    group.add(walls);
  }

  q.identity();
  if (level.pillars.length > 0) {
    const pillars = new InstancedMesh(new BoxGeometry(1, 1, 1), stoneMat, level.pillars.length);
    level.pillars.forEach((r, i) => {
      const x = r.x + r.w / 2;
      const z = r.z + r.d / 2;
      pillars.setMatrixAt(i, m.compose(p.set(x, heightAt(x, z) + PILLAR_H / 2, z), q, s.set(r.w, PILLAR_H, r.d)));
    });
    pillars.castShadow = pillars.receiveShadow = true;
    group.add(pillars);
  }

  if (level.bumpers.length > 0) {
    const bumpers = new InstancedMesh(
      new CylinderGeometry(1, 1, BUMPER_H, BUMPER_SEGMENTS),
      new MeshStandardMaterial({map: tex.wood, roughness: WOOD_ROUGHNESS}),
      level.bumpers.length
    );
    level.bumpers.forEach((b, i) => {
      bumpers.setMatrixAt(i, m.compose(p.set(b.x, heightAt(b.x, b.z) + BUMPER_H / 2, b.z), q, s.set(b.r, 1, b.r)));
    });
    bumpers.castShadow = bumpers.receiveShadow = true;
    group.add(bumpers);
  }
}
