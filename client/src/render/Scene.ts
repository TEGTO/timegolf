import {Color, PCFShadowMap, Scene, Vector3, WebGLRenderer} from 'three';
import type {ScreenPoint} from '../input';
import {clock, focus, update as updatePlayback} from '../playback';
import {listener} from '../sfx';
import {UNKNOWN_PLAYER_COLOR} from '../constants';
import {aimMode, playerOf, useStore, type AimMode} from '../store';
import {Aim} from './Aim';
import {Balls} from './Balls';
import {CameraRig} from './Camera';
import {swayFlag} from './Hole';
import {buildLevel, type LevelView} from './LevelMesh';
import {Lights} from './Lights';
import {paletteOf} from './palettes';
import {FuturePath} from './Path';
import {texturesFor} from './textures';
import {Watch} from './Watch';

const MAX_PIXEL_RATIO = 2;
/** Shown until the first level arrives. */
const NO_LEVEL_BACKGROUND = '#cdeeff';
// A stalled frame (background tab, debugger) shouldn't jump playback or the camera in one step.
const MAX_FRAME_MS = 100;
// Own ball grab radius: at least this many px, more once zoomed in far enough that GRAB_WORLD covers more.
const GRAB_PX = 30;
const GRAB_WORLD = 0.45;

/** three.js scene: level, balls, aim, paths and camera; driven by the store and by input intents. */
export class GameScene {
  private readonly renderer: WebGLRenderer;
  private readonly scene = new Scene();
  private readonly rig: CameraRig;
  private readonly lights: Lights;
  private readonly balls: Balls;
  private readonly aim: Aim;
  private readonly path: FuturePath;
  private readonly watch: Watch;
  private view: LevelView | null = null;
  private readonly ballPos = new Vector3();
  private mode: AimMode = null;
  private width = 1;
  private height = 1;
  private frame = 0;
  private last = performance.now();
  private readonly unsubscribe: () => void;
  private readonly resizeObserver: ResizeObserver;

  constructor(
    private readonly container: HTMLElement,
    onShoot: (dirX: number, dirZ: number, power: number) => void,
    onAim: (dirX: number, dirZ: number, power: number | null) => void
  ) {
    this.renderer = new WebGLRenderer({antialias: true});
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, MAX_PIXEL_RATIO));
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = PCFShadowMap;
    container.appendChild(this.renderer.domElement);
    this.rig = new CameraRig(this.renderer.domElement);

    this.lights = new Lights(this.scene, this.renderer.capabilities.maxTextureSize);
    this.scene.background = new Color(NO_LEVEL_BACKGROUND);

    const radius = useStore.getState().config.ballRadius;
    this.balls = new Balls(this.scene, radius);
    this.aim = new Aim(this.scene, radius, onShoot, onAim);
    this.path = new FuturePath(this.scene, container);
    this.watch = new Watch(this.scene, radius);

    this.resizeObserver = new ResizeObserver(this.resize);
    this.resizeObserver.observe(container);
    this.resize();

    this.unsubscribe = useStore.subscribe((s, prev) => {
      if (s.level !== prev.level) this.setLevel();
      if (s.players !== prev.players || s.turn !== prev.turn) this.applyPlayerColors();
      if (
        s.level !== prev.level ||
        s.viewed !== prev.viewed ||
        s.timeline !== prev.timeline ||
        s.players !== prev.players ||
        s.changed !== prev.changed ||
        s.playing !== prev.playing
      )
        this.showPath();
      else if (s.hovered !== prev.hovered) this.path.highlight(s.hovered);
      if (s.playing !== prev.playing || s.turn !== prev.turn || s.viewed !== prev.viewed) this.rig.follow = true;
      this.setMode(aimMode(s));
    });
    this.setLevel();
    this.applyPlayerColors();
    this.showPath();
    this.setMode(aimMode(useStore.getState()));
    this.frame = requestAnimationFrame(this.tick);
  }

  /** Where input listens for pointer and wheel events. */
  get canvas(): HTMLElement {
    return this.renderer.domElement;
  }

  focus() {
    this.rig.follow = true;
  }

  rotate(steps: number) {
    this.rig.rotate(steps);
  }

  rotateBy(dxPx: number) {
    this.rig.rotateBy(dxPx);
  }

  zoomBy(wheelDeltaY: number) {
    this.rig.zoomBy(wheelDeltaY);
  }

  pan(from: ScreenPoint, to: ScreenPoint) {
    this.rig.pan(from, to);
  }

  /** Starts aiming when `at` grabs my aimable ball. */
  beginAim(at: ScreenPoint) {
    return this.aim.canAim && this.canGrab(at) && this.aim.start();
  }

  dragAim(at: ScreenPoint) {
    if (this.aim.isAiming) this.aim.drag(this.rig.ray(at));
  }

  /** `dragged` is false for a click without a real drag, which must never shoot. */
  endAim(dragged: boolean) {
    this.aim.release(dragged);
  }

  /** False when there was no aim to cancel. */
  cancelAim() {
    return this.aim.cancel();
  }

  private canGrab(at: ScreenPoint) {
    const grab = Math.max(GRAB_PX, GRAB_WORLD / this.rig.worldPerPixel());
    return this.rig.screenDistance(at, this.aim.origin) <= grab;
  }

  private setMode(mode: AimMode) {
    this.mode = mode;
    this.aim.setOriginal(mode?.kind === 'replace' ? mode.record : null);
  }

  private setLevel() {
    const {level, config} = useStore.getState();
    if (this.view) {
      this.scene.remove(this.view.group);
      this.view.dispose();
      this.view = null;
    }
    if (!level) return;
    const view = buildLevel(level, config.holeRadius);
    this.view = view;
    this.scene.add(view.group);
    this.scene.background = texturesFor(level.palette).background;
    this.lights.setPalette(paletteOf(level.palette));
    this.lights.fit(view);

    const sp = level.spawns[0];
    const target = this.rig.target;
    target.set(sp?.[0] ?? view.center.x, 0, sp?.[1] ?? view.center.z);
    target.y = view.heightAt(target.x, target.z);
    this.rig.follow = true;
  }

  private showPath() {
    const s = useStore.getState();
    // Paths only when looking into the past, so a plain continuation from the present stays uncluttered.
    const from = s.playing ? null : s.viewed;
    const ghost = from !== null && s.changed && from <= s.changed.index ? s.changed : null;
    this.path.show(this.view, s.timeline, from, s.me, s.players, ghost);
    this.path.highlight(s.hovered);
  }

  private applyPlayerColors() {
    const s = useStore.getState();
    this.balls.syncColors(s.players);
    this.watch.setColor(playerOf(s, s.turn?.playerId ?? null)?.color ?? UNKNOWN_PLAYER_COLOR);
  }

  /** Writes the point the camera should follow into `ballPos`: the playback focus, the watched player's (ghost) ball, my ball, else the turn's ball. */
  private followPoint(view: LevelView, own: boolean, watching: boolean, turnId: string | null) {
    if (focus.active) {
      this.ballPos.set(focus.x, view.heightAt(focus.x, focus.z), focus.z);
      return true;
    }
    if (watching) {
      this.ballPos.copy(this.watch.origin);
      return true;
    }
    if (own) {
      this.ballPos.copy(this.aim.origin);
      return true;
    }
    return this.balls.position(turnId, view, this.ballPos);
  }

  private readonly tick = (now: number) => {
    this.frame = requestAnimationFrame(this.tick);
    const dtMs = Math.min(MAX_FRAME_MS, now - this.last);
    this.last = now;
    const view = this.view;
    let chase: Vector3 | null = null;
    if (view) {
      updatePlayback(dtMs, now);
      const s = useStore.getState();
      const turnId = s.turn?.playerId ?? null;
      this.balls.update(view, s.me, turnId, now);
      const own = this.balls.position(s.me, view, this.aim.origin);
      this.aim.canAim = this.mode !== null && own;
      this.aim.update();
      const watching = this.watch.update(view, s, this.balls, dtMs);
      if (this.rig.follow && this.followPoint(view, own, watching, turnId)) chase = this.ballPos;
      swayFlag(view.flag, now);
      for (let i = 0; i < view.windmills.length; i++) {
        const w = view.windmills[i];
        w.blades.rotation.z = w.phase + s.config.windmillOmega * clock.t;
      }
    }
    this.rig.update(dtMs, chase);
    listener.x = this.rig.target.x;
    listener.z = this.rig.target.z;
    this.renderer.render(this.scene, this.rig.camera);
    this.path.update(this.rig.camera, this.width, this.height);
  };

  private readonly resize = () => {
    const w = Math.max(1, this.container.clientWidth);
    const h = Math.max(1, this.container.clientHeight);
    this.width = w;
    this.height = h;
    this.renderer.setSize(w, h);
    this.rig.resize(w, h);
  };

  dispose() {
    cancelAnimationFrame(this.frame);
    this.unsubscribe();
    this.resizeObserver.disconnect();
    this.view?.dispose();
    this.balls.dispose();
    this.aim.dispose();
    this.path.dispose();
    this.watch.dispose();
    this.renderer.dispose();
    this.renderer.domElement.remove();
  }
}
