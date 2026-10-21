import {BoxGeometry, BufferAttribute, BufferGeometry, CylinderGeometry, Group, Mesh, MeshStandardMaterial} from 'three';
import type {WindmillDto} from '../types';
import {STONE_ROUGHNESS, WOOD_ROUGHNESS} from './constants';
import type {Palette} from './palettes';
import {sailTexture, type TextureSet} from './textures';

// Footprint matches the physics base (§4.1): 3 across travel × 1.2 along it, 1-wide tunnel.
const WIDTH = 3;
const DEPTH = 1.2;
const TUNNEL_W = 1;
const TUNNEL_H = 0.5;
const WALL_H = 1.75;
const ROOF_H = 0.9;
const OVERHANG = 0.16;
const BLADE_R = 1.4;
// Hub just high enough that a blade pointing down sweeps the tunnel mouth, as the physics gate expects.
const HUB_Y = BLADE_R + 0.08;
const SAIL_FROM = 0.3;
const SAIL_W = 0.36;
const SAIL_T = 0.03;
// Sail sits just beside its spar and slightly behind it, so the spar reads in front.
const SAIL_GAP = 0.03;
const SAIL_BEHIND = 0.02;
const SPAR_W = 0.07;
const SPAR_T = 0.06;
const HUB_R = 0.17;
const HUB_T = 0.16;
const HUB_SEGMENTS = 20;
/** Hub distance in front of the house's face. */
const HUB_OUT = 0.14;
const BLADES = 4;
const TIMBER = '#7a4a2c';
const TIMBER_ROUGHNESS = 0.7;
const SAIL_ROUGHNESS = 0.8;

export type WindmillView = {blades: Group; phase: number};

/** UVs from world-space position per face direction, so bricks keep one size and line up across pieces. */
function brickBox(w: number, h: number, d: number, x: number, y: number) {
  const g = new BoxGeometry(w, h, d).translate(x, y, 0);
  const pos = g.getAttribute('position');
  const nrm = g.getAttribute('normal');
  const uv = g.getAttribute('uv');
  for (let i = 0; i < pos.count; i++) {
    const nx = Math.abs(nrm.getX(i));
    const ny = Math.abs(nrm.getY(i));
    if (nx > 0.5) uv.setXY(i, pos.getZ(i), pos.getY(i));
    else if (ny > 0.5) uv.setXY(i, pos.getX(i), pos.getZ(i));
    else uv.setXY(i, pos.getX(i), pos.getY(i));
  }
  return g;
}

function roofGeometry() {
  const x = WIDTH / 2 + OVERHANG;
  const z = DEPTH / 2 + OVERHANG;
  const y0 = WALL_H;
  const y1 = WALL_H + ROOF_H;
  // prettier-ignore
  const p = [
    -x, y0, z, x, y0, z, x, y1, 0, -x, y0, z, x, y1, 0, -x, y1, 0, // front slope
    x, y0, -z, -x, y0, -z, -x, y1, 0, x, y0, -z, -x, y1, 0, x, y1, 0, // back slope
    x, y0, z, x, y0, -z, x, y1, 0, // gables
    -x, y0, -z, -x, y0, z, -x, y1, 0,
    // Underside: both slopes face the sun, and casters only draw back faces into the shadow map.
    -x, y0, z, -x, y0, -z, x, y0, -z, -x, y0, z, x, y0, -z, x, y0, z
  ];
  const g = new BufferGeometry();
  g.setAttribute('position', new BufferAttribute(new Float32Array(p), 3));
  g.computeVertexNormals();
  return g;
}

export function buildWindmills(
  windmills: WindmillDto[],
  heightAt: (x: number, z: number) => number,
  pal: Palette,
  tex: TextureSet,
  parent: Group
): WindmillView[] {
  if (windmills.length === 0) return [];
  const stone = new MeshStandardMaterial({map: tex.stone, roughness: STONE_ROUGHNESS});
  const roof = new MeshStandardMaterial({color: pal.wood, roughness: WOOD_ROUGHNESS, flatShading: true});
  const timber = new MeshStandardMaterial({color: TIMBER, roughness: TIMBER_ROUGHNESS});
  const sail = new MeshStandardMaterial({map: sailTexture(), roughness: SAIL_ROUGHNESS});
  const side = (WIDTH - TUNNEL_W) / 2;
  const house = [
    brickBox(side, WALL_H, DEPTH, -(TUNNEL_W + side) / 2, WALL_H / 2),
    brickBox(side, WALL_H, DEPTH, (TUNNEL_W + side) / 2, WALL_H / 2),
    brickBox(TUNNEL_W, WALL_H - TUNNEL_H, DEPTH, 0, (WALL_H + TUNNEL_H) / 2)
  ];
  const roofGeo = roofGeometry();
  const hubGeo = new CylinderGeometry(HUB_R, HUB_R, HUB_T, HUB_SEGMENTS).rotateX(Math.PI / 2);
  const sparGeo = new BoxGeometry(SPAR_W, BLADE_R, SPAR_T).translate(0, -BLADE_R / 2, 0);
  const sailGeo = new BoxGeometry(SAIL_W, BLADE_R - SAIL_FROM, SAIL_T).translate(
    SAIL_W / 2 + SAIL_GAP,
    -(SAIL_FROM + BLADE_R) / 2,
    -SAIL_BEHIND
  );

  const shadowed = (mesh: Mesh) => {
    mesh.castShadow = mesh.receiveShadow = true;
    return mesh;
  };
  return windmills.map((w) => {
    const root = new Group();
    root.position.set(w.x, heightAt(w.x, w.z), w.z);
    // Local +z is the travel axis; it maps to world +x or +z, both toward the camera, so the blades stay in view.
    root.rotation.y = w.axis === 0 ? Math.PI / 2 : 0;
    for (const g of house) root.add(shadowed(new Mesh(g, stone)));
    root.add(shadowed(new Mesh(roofGeo, roof)));
    const blades = new Group();
    blades.position.set(0, HUB_Y, DEPTH / 2 + HUB_OUT);
    blades.add(shadowed(new Mesh(hubGeo, timber)));
    for (let k = 0; k < BLADES; k++) {
      // Blade k points straight down at θ + k·π/2 = 0 (mod 2π): the gate-closed pose.
      const arm = new Group();
      arm.rotation.z = (k * 2 * Math.PI) / BLADES;
      arm.add(shadowed(new Mesh(sparGeo, timber)), shadowed(new Mesh(sailGeo, sail)));
      blades.add(arm);
    }
    root.add(blades);
    parent.add(root);
    return {blades, phase: w.phase};
  });
}
