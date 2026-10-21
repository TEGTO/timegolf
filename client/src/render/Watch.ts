import {Mesh, MeshStandardMaterial, SphereGeometry, Vector3, type Object3D} from 'three';
import type {State} from '../store';
import type {BallPosDto} from '../types';
import {Arrow, ARROW_HEIGHT, MIN_POWER} from './Aim';
import type {Balls} from './Balls';
import type {LevelView} from './LevelMesh';
import {ballTexture} from './textures';

const GHOST_OPACITY = 0.55;
const GHOST_SEGMENTS = 20;
const ARROW_OPACITY = 0.55;
// Updates arrive ~10/s; easing (per ms) toward the latest one hides the steps without lagging behind.
const AIM_EASE_RATE = 0.02;

function find(list: BallPosDto[], id: string) {
  for (let i = 0; i < list.length; i++) if (list[i].playerId === id) return list[i];
  return undefined;
}

/** Another player's live turn (§10.2): a ghost ball at the checkpoint they view and their aim arrow. */
export class Watch {
  /** Their (ghost) ball; valid while `update` returns true. */
  readonly origin = new Vector3();
  private readonly ghost: Mesh<SphereGeometry, MeshStandardMaterial>;
  private readonly arrow = new Arrow(ARROW_OPACITY);
  /** False until the first update of an aim, which snaps instead of easing in from a stale one. */
  private easing = false;
  private dirX = 1;
  private dirZ = 0;
  private power = 0;

  constructor(
    private readonly parent: Object3D,
    private readonly radius: number
  ) {
    this.ghost = new Mesh(
      new SphereGeometry(radius, GHOST_SEGMENTS, GHOST_SEGMENTS),
      new MeshStandardMaterial({map: ballTexture(), transparent: true, opacity: GHOST_OPACITY, depthWrite: false})
    );
    this.ghost.visible = false;
    parent.add(this.ghost, this.arrow.group);
  }

  setColor(color: string) {
    this.ghost.material.color.setStyle(color);
    this.arrow.material.color.setStyle(color);
  }

  /** Per frame; false when it isn't someone else's turn or their ball is gone. */
  update(view: LevelView, s: State, balls: Balls, dtMs: number) {
    this.ghost.visible = false;
    this.arrow.group.visible = false;
    const id = s.turn?.playerId;
    if (!id || id === s.me || s.playing) return false;
    const aim = s.watched;
    const theirs = aim?.viewIndex ?? null;
    const record = theirs === null ? undefined : s.timeline[theirs];
    const viewIndex = record ? theirs : null;
    if (viewIndex === s.viewed) {
      if (!balls.position(id, view, this.origin)) return false;
    } else {
      // They look at another moment than I do: their ball there, without moving mine.
      const p = find(record?.start ?? s.balls, id);
      if (!p || p.holed) return false;
      this.origin.set(p.x, view.heightAt(p.x, p.z) + this.radius, p.z);
      this.ghost.position.copy(this.origin);
      this.ghost.visible = true;
    }
    if (aim?.power == null) {
      this.easing = false;
      return true;
    }
    if (!this.easing) {
      this.dirX = aim.dirX;
      this.dirZ = aim.dirZ;
      this.power = aim.power;
    } else {
      const k = 1 - Math.exp(-dtMs * AIM_EASE_RATE);
      this.dirX += (aim.dirX - this.dirX) * k;
      this.dirZ += (aim.dirZ - this.dirZ) * k;
      this.power += (aim.power - this.power) * k;
    }
    this.easing = true;
    if (this.power > MIN_POWER) {
      this.arrow.place(this.origin, this.radius - ARROW_HEIGHT, this.dirX, this.dirZ, this.power);
      this.arrow.group.visible = true;
    }
    return true;
  }

  dispose() {
    this.parent.remove(this.ghost, this.arrow.group);
    this.ghost.geometry.dispose();
    this.ghost.material.dispose();
    this.arrow.dispose();
  }
}
