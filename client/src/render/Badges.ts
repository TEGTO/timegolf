import {Vector3, type Camera} from 'three';

const GAP_PX = 2;

type Badge = {index: number; at: Vector3; el: HTMLElement; w: number; h: number; x: number; y: number};

/** DOM overlay of path badges pinned to world points; `index` is the hit each badge belongs to. */
export class PathBadges {
  private list: Badge[] = [];
  private readonly overlay = document.createElement('div');
  private readonly probe = new Vector3();

  constructor(host: HTMLElement) {
    this.overlay.className = 'path-badges';
    host.appendChild(this.overlay);
  }

  add(index: number, at: Vector3, text: string, color: string, cause: boolean) {
    const el = document.createElement('span');
    el.className = cause ? 'path-badge cause' : 'path-badge';
    const dot = document.createElement('i');
    dot.style.background = color;
    el.append(dot, text);
    this.overlay.appendChild(el);
    this.list.push({index, at, el, w: 0, h: 0, x: NaN, y: NaN});
  }

  /** Once after adding, so `update` never reads layout. */
  measure() {
    for (const b of this.list) {
      b.w = b.el.offsetWidth;
      b.h = b.el.offsetHeight;
    }
  }

  has(index: number) {
    return this.list.some((b) => b.index === index);
  }

  /** With `index` set, every other hit's badges fade. */
  focus(index: number | null) {
    for (const b of this.list) b.el.classList.toggle('dim', index !== null && b.index !== index);
  }

  /** Per frame after render: projects badges to the screen, stacking any that would overlap; DOM writes only on change. */
  update(camera: Camera, width: number, height: number) {
    const list = this.list;
    for (let k = 0; k < list.length; k++) {
      const b = list[k];
      this.probe.copy(b.at).project(camera);
      const x = Math.round(((this.probe.x + 1) / 2) * width);
      let y = Math.round(((1 - this.probe.y) / 2) * height);
      for (let j = 0; j < k; j++) {
        const o = list[j];
        if (Math.abs(x - o.x) * 2 < b.w + o.w && Math.abs(y - o.y) < o.h + GAP_PX) {
          y = o.y - o.h - GAP_PX;
          j = -1;
        }
      }
      if (x === b.x && y === b.y) continue;
      b.x = x;
      b.y = y;
      b.el.style.transform = `translate(${x}px, ${y}px)`;
    }
  }

  clear() {
    this.list = [];
    this.overlay.replaceChildren();
  }

  dispose() {
    this.clear();
    this.overlay.remove();
  }
}
