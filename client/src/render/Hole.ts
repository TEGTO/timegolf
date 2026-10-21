import {
  CircleGeometry,
  CylinderGeometry,
  DoubleSide,
  Mesh,
  MeshBasicMaterial,
  MeshStandardMaterial,
  PlaneGeometry,
  RingGeometry,
  Vector3,
  type Group
} from 'three';

const CUP_COLOR = '#0b0d0c';
const RIM_COLOR = '#f4f1e8';
const RIM_W = 0.035;
const RIM_OPACITY = 0.85;
const CUP_SEGMENTS = 32;
// Cup and rim float just above the green, rim over cup, so neither z-fights.
const CUP_LIFT = 0.006;
const RIM_LIFT = 0.007;
const POLE_WHITE = '#fbfaf6';
const POLE_R = 0.018;
const POLE_H = 1.3;
const POLE_SEGMENTS = 8;
const FLAG_RED = '#e8352b';
const FLAG_W = 0.46;
const FLAG_H = 0.28;
const FLAG_Y = 1.14;
const FLAG_ROUGHNESS = 0.7;
const FLAG_SWAY_MS = 600;
const FLAG_SWAY_RAD = 0.25;

/** Cup, rim, pole and flag at `hole` (already on the surface), added to `group`; returns the flag to animate. */
export function buildHole(hole: Vector3, holeRadius: number, group: Group) {
  const cup = new Mesh(new CircleGeometry(holeRadius, CUP_SEGMENTS), new MeshBasicMaterial({color: CUP_COLOR}));
  cup.rotation.x = -Math.PI / 2;
  cup.position.copy(hole).y += CUP_LIFT;
  const rim = new Mesh(
    new RingGeometry(holeRadius, holeRadius + RIM_W, CUP_SEGMENTS),
    new MeshBasicMaterial({color: RIM_COLOR, transparent: true, opacity: RIM_OPACITY})
  );
  rim.rotation.x = -Math.PI / 2;
  rim.position.copy(hole).y += RIM_LIFT;
  const pole = new Mesh(
    new CylinderGeometry(POLE_R, POLE_R, POLE_H, POLE_SEGMENTS),
    new MeshStandardMaterial({color: POLE_WHITE})
  );
  pole.position.copy(hole).y += POLE_H / 2;
  pole.castShadow = true;
  const flag = new Mesh(
    new PlaneGeometry(FLAG_W, FLAG_H),
    new MeshStandardMaterial({color: FLAG_RED, side: DoubleSide, roughness: FLAG_ROUGHNESS})
  );
  // Hinge on the pole: the plane's left edge sits at the flag's origin.
  flag.geometry.translate(FLAG_W / 2, 0, 0);
  flag.position.copy(hole).y += FLAG_Y;
  flag.castShadow = true;
  group.add(cup, rim, pole, flag);
  return flag;
}

/** Per frame: gentle back-and-forth around the pole. */
export function swayFlag(flag: Mesh, now: number) {
  flag.rotation.y = Math.sin(now / FLAG_SWAY_MS) * FLAG_SWAY_RAD;
}
