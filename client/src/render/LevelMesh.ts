import {Box3, Group, Mesh, Vector3, type Material} from 'three';
import type {LevelDto} from '../types';
import {buildBunkers} from './Bunker';
import {buildHole} from './Hole';
import {buildObstacles} from './Obstacles';
import {paletteOf} from './palettes';
import {buildTerrain} from './Terrain';
import {texturesFor} from './textures';
import {buildWindmills, type WindmillView} from './Windmill';

// Above the windmill roof ridge: everything that can cast a shadow fits under it.
const TOP_Y = 2.8;
/** Bounding sphere slack beyond the level's half diagonal. */
const RADIUS_MARGIN = 1;

export type LevelView = {
  group: Group;
  flag: Mesh;
  windmills: WindmillView[];
  hole: Vector3;
  center: Vector3;
  radius: number;
  bounds: Box3;
  heightAt(x: number, z: number): number;
  dispose(): void;
};

export function buildLevel(level: LevelDto, holeRadius: number): LevelView {
  const {width: w, height: h} = level;
  const pal = paletteOf(level.palette);
  const tex = texturesFor(level.palette);
  const group = new Group();

  const {heightAt, minY, baseY} = buildTerrain(level, pal, tex, group);
  const bunkerTextures = buildBunkers(level, heightAt, pal, group);
  buildObstacles(level, heightAt, tex, group);
  const hole = new Vector3(level.hole.x, heightAt(level.hole.x, level.hole.z), level.hole.z);
  const flag = buildHole(hole, holeRadius, group);
  const windmills = buildWindmills(level.windmills, heightAt, pal, tex, group);

  return {
    group,
    flag,
    windmills,
    hole,
    center: new Vector3(w / 2, minY / 2, h / 2),
    radius: Math.hypot(w, h) / 2 + RADIUS_MARGIN,
    bounds: new Box3(new Vector3(0, baseY, 0), new Vector3(w, TOP_Y, h)),
    heightAt,
    dispose() {
      for (const t of bunkerTextures) t.dispose();
      group.traverse((o) => {
        if (o instanceof Mesh) {
          o.geometry.dispose();
          (o.material as Material).dispose();
        }
      });
    }
  };
}
