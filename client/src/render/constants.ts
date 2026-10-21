import {Vector3} from 'three';

/** World up; shared read-only, never mutate. */
export const UP = new Vector3(0, 1, 0);

/** Toward the sun: the light and the bunker bank shading must agree on it. */
export const SUN_DIR = new Vector3(-4, 10, -6).normalize();

/** Transparent ground overlays: bunker decals first, the changed-future ghost, then paths / arrows / rings, a hovered path on top. */
export const RENDER_ORDER = {decal: -1, ghost: 1, overlay: 2, highlight: 3} as const;

export const STONE_ROUGHNESS = 0.85;
export const WOOD_ROUGHNESS = 0.6;
