/** Screen position of a pointer; a PointerEvent qualifies as is. */
export type ScreenPoint = {clientX: number; clientY: number};

/** One-shot intents. A handler returning false means "doesn't apply right now", so the next bound intent gets a go. */
type TapIntent =
  'cancelAim' | 'present' | 'viewPrev' | 'viewNext' | 'skip' | 'focus' | 'rotateLeft' | 'rotateRight' | 'mute';

/** Press-move-release gestures on the canvas. */
type DragIntent = 'aim' | 'freeRotate' | 'pan';

type Intent = TapIntent | DragIntent;

/** `begin` returning false declines the press; `dragged` tells a real drag from a click. */
export type Drag = {
  begin?(at: ScreenPoint): boolean;
  move(from: ScreenPoint, to: ScreenPoint): void;
  end?(dragged: boolean): void;
};

export type InputHandlers = Record<TapIntent, () => boolean | void> &
  Record<DragIntent, Drag> & {zoom(deltaY: number): void};

/** Intents tried in order until one applies; `repeat: false` ignores key auto-repeat. */
type Binding<T extends Intent> = {intents: readonly T[]; preventDefault?: boolean; repeat?: false};

const Button = {left: 0, middle: 1, right: 2} as const;

/** PRD §12 controls table. */
const KEY_BINDINGS: Readonly<Record<string, Binding<TapIntent> | undefined>> = {
  ArrowLeft: {intents: ['viewPrev'], preventDefault: true},
  ArrowRight: {intents: ['viewNext'], preventDefault: true},
  Escape: {intents: ['cancelAim', 'present']},
  // Holding S would otherwise send a Skip per auto-repeat, each rejected with a toast.
  KeyS: {intents: ['skip'], repeat: false},
  KeyF: {intents: ['focus']},
  KeyQ: {intents: ['rotateLeft']},
  KeyE: {intents: ['rotateRight']},
  KeyM: {intents: ['mute']}
};

const BUTTON_BINDINGS: Readonly<Record<number, Binding<Intent> | undefined>> = {
  // Only a drag shoots: Aim fires on release only when `dragged`.
  [Button.left]: {intents: ['aim']},
  // Keeps the browser's middle-click autoscroll out of the way.
  [Button.middle]: {intents: ['freeRotate'], preventDefault: true},
  [Button.right]: {intents: ['cancelAim', 'pan']}
};

/** Movement past this many px turns a press into a drag. */
const CLICK_SLOP_PX = 6;
// `PointerEvent.buttons` bit per `button` index.
const BUTTON_BIT = [1, 4, 2];

const isDrag = (intent: Intent): intent is DragIntent =>
  intent === 'aim' || intent === 'freeRotate' || intent === 'pan';

/** Keyboard on `window` (except while typing), pointer and wheel on `canvas`; returns the unbind function. */
export function bindInput(canvas: HTMLElement, on: InputHandlers) {
  let drag: {intent: DragIntent; button: number; dragged: boolean} | null = null;
  const start = {clientX: 0, clientY: 0};
  const last = {clientX: 0, clientY: 0};

  const tap = (intent: TapIntent) => on[intent]() !== false;

  const press = (e: PointerEvent) => {
    const binding = BUTTON_BINDINGS[e.button];
    if (!binding) return;
    if (binding.preventDefault) e.preventDefault();
    for (const intent of binding.intents) {
      if (!isDrag(intent)) {
        if (tap(intent)) return;
        continue;
      }
      if (drag || on[intent].begin?.(e) === false) continue;
      drag = {intent, button: e.button, dragged: false};
      start.clientX = last.clientX = e.clientX;
      start.clientY = last.clientY = e.clientY;
      canvas.setPointerCapture(e.pointerId);
      return;
    }
  };

  const release = (e: PointerEvent) => {
    if (!drag || e.button !== drag.button) return;
    const {intent, dragged} = drag;
    drag = null;
    on[intent].end?.(dragged);
  };

  const onPointerMove = (e: PointerEvent) => {
    // A second button pressed or released mid-drag arrives as a move (chorded buttons), never as pointerdown/up.
    if (e.button >= 0) {
      if (e.buttons & BUTTON_BIT[e.button]) press(e);
      else release(e);
    }
    if (!drag) return;
    if (Math.hypot(e.clientX - start.clientX, e.clientY - start.clientY) > CLICK_SLOP_PX) drag.dragged = true;
    on[drag.intent].move(last, e);
    last.clientX = e.clientX;
    last.clientY = e.clientY;
  };

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.target instanceof HTMLInputElement) return;
    const binding = KEY_BINDINGS[e.code];
    if (!binding || (e.repeat && binding.repeat === false)) return;
    if (binding.preventDefault) e.preventDefault();
    binding.intents.some(tap);
  };

  const onWheel = (e: WheelEvent) => {
    e.preventDefault();
    on.zoom(e.deltaY);
  };

  const onContextMenu = (e: Event) => e.preventDefault();

  window.addEventListener('keydown', onKeyDown);
  canvas.addEventListener('pointerdown', press);
  canvas.addEventListener('pointermove', onPointerMove);
  canvas.addEventListener('pointerup', release);
  canvas.addEventListener('wheel', onWheel, {passive: false});
  canvas.addEventListener('contextmenu', onContextMenu);
  return () => {
    window.removeEventListener('keydown', onKeyDown);
    canvas.removeEventListener('pointerdown', press);
    canvas.removeEventListener('pointermove', onPointerMove);
    canvas.removeEventListener('pointerup', release);
    canvas.removeEventListener('wheel', onWheel);
    canvas.removeEventListener('contextmenu', onContextMenu);
  };
}
