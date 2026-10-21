import {play, playBounce} from './sfx';
import {useStore} from './store';
import type {BallPosDto, BounceKind, HitPlayDto} from './types';

const TRACK_MS = 50;
const SINK_MS = 380;
// Any sink in (0, 1) means "sinking"; starting just above 0 lets the animation run its full length.
const SINK_START = 0.001;
const MAX_BOUNCE_SOUNDS = 4;
const BOUNCE_WINDOW_MS = 50;
// Frames run late or not at all (background tab); past this lag playback skips to the end instead of stalling turns.
const MAX_LAG_MS = 400;

/** sink: 0 on the course, (0, 1) sinking into the cup, 1 gone. */
export type BallView = {playerId: string; x: number; z: number; holed: boolean; sink: number};

type Run = {ball: BallView; points: number[]; sinks: boolean; done: boolean};
type Item = {hit: HitPlayDto; speed: number};

/** What the scene draws: the running playback, or else the viewed checkpoint / present. */
export const world: BallView[] = [];
/** World clock in seconds (drives windmills): running record's sim time, else frozen at the viewed checkpoint / present. */
export const clock = {t: 0};
/** Ball the camera should follow while playback runs. */
export const focus = {x: 0, z: 0, active: false};

const queue: Item[] = [];
let current: Item | null = null;
let runs: Run[] = [];
let t = 0;
let lengthMs = 0;
let nextBounce = 0;
let windowStart = 0;
let windowCount = 0;
/** Real time (performance.now) when everything queued should have finished playing. */
let deadline = 0;
let watchdog = 0;

export function ballOf(id: string | null) {
  for (let i = 0; i < world.length; i++) if (world[i].playerId === id) return world[i];
  return undefined;
}

function place(p: BallPosDto) {
  let b = ballOf(p.playerId);
  if (!b) {
    b = {playerId: p.playerId, x: 0, z: 0, holed: false, sink: 0};
    world.push(b);
  }
  b.x = p.x;
  b.z = p.z;
  // A ball already sinking keeps its animation; one that appears holed out of nowhere is just gone.
  if (!p.holed) b.sink = 0;
  else if (!b.holed) b.sink = 1;
  b.holed = p.holed;
  return b;
}

function placeAll(list: BallPosDto[]) {
  for (const p of list) place(p);
}

function showStatic() {
  const s = useStore.getState();
  placeAll(s.viewed === null ? s.balls : (s.timeline[s.viewed]?.start ?? s.balls));
}

function lengthOf(hit: HitPlayDto) {
  if (hit.void) return 0;
  let ms = hit.durationMs;
  for (const track of hit.tracks) ms = Math.max(ms, (track.points.length / 2 - 1) * TRACK_MS);
  return ms;
}

function stop() {
  queue.length = 0;
  current = null;
  runs = [];
  focus.active = false;
  clearTimeout(watchdog);
  if (useStore.getState().playing) useStore.setState({playing: false, shot: null});
}

/** Ends playback at once: balls jump to the final state of everything queued. */
export function flush() {
  const last = queue[queue.length - 1] ?? current;
  if (last) for (const p of last.hit.end) place(p).sink = p.holed ? 1 : 0;
  stop();
  showStatic();
}

function reset() {
  world.length = 0;
  stop();
  showStatic();
}

useStore.subscribe((s, prev) => {
  if (s.level !== prev.level) reset();
  else if (!current && !s.playing && (s.viewed !== prev.viewed || s.balls !== prev.balls || s.playing !== prev.playing))
    showStatic();
});

/** Must run before the store applies the new timeline, so the present doesn't flash in ahead of the animation. */
export function enqueue(hits: HitPlayDto[], replaced: boolean) {
  if (hits.length === 0) return;
  const speed = useStore.getState().config.replaySpeed;
  const now = performance.now();
  deadline = Math.max(deadline, now);
  hits.forEach((hit, i) => {
    const item = {hit, speed: replaced && i > 0 ? speed : 1};
    queue.push(item);
    deadline += lengthOf(hit) / item.speed;
  });
  const first = hits[0];
  useStore.setState({playing: true, shot: {playerId: first.playerId, index: first.index, replaced}});
  // rAF doesn't run in a hidden tab, so nothing else would ever end this playback.
  clearTimeout(watchdog);
  if (document.hidden) flush();
  else watchdog = window.setTimeout(lagCheck, deadline - now + MAX_LAG_MS);
}

function lagCheck() {
  if (!useStore.getState().playing) return;
  const early = deadline + MAX_LAG_MS - performance.now();
  if (early < 0) flush();
  else watchdog = window.setTimeout(lagCheck, early + 1);
}

document.addEventListener('visibilitychange', () => document.hidden && flush());

function begin() {
  const item = queue.shift();
  if (!item) {
    stop();
    return null;
  }
  current = item;
  const {hit} = item;
  t = 0;
  nextBounce = 0;
  lengthMs = lengthOf(hit);
  runs = [];
  placeAll(hit.start);
  if (hit.void) return item;
  for (const track of hit.tracks) {
    const ball = ballOf(track.playerId);
    if (!ball || track.points.length < 2) continue;
    const sinks = !ball.holed && !!hit.end.find((e) => e.playerId === track.playerId)?.holed;
    runs.push({ball, points: track.points, sinks, done: false});
  }
  const owner = ballOf(hit.playerId);
  play('hit', owner?.x, owner?.z);
  return item;
}

function advance(run: Run, ownerId: string) {
  const {ball, points} = run;
  const f = t / TRACK_MS;
  const last = points.length / 2 - 1;
  if (f >= last) {
    ball.x = points[last * 2];
    ball.z = points[last * 2 + 1];
    run.done = true;
    if (run.sinks) {
      ball.holed = true;
      ball.sink = SINK_START;
      play('hole', ball.x, ball.z);
    }
    return;
  }
  const i = Math.floor(f);
  const a = f - i;
  ball.x = points[i * 2] + (points[i * 2 + 2] - points[i * 2]) * a;
  ball.z = points[i * 2 + 1] + (points[i * 2 + 3] - points[i * 2 + 1]) * a;
  if (!focus.active || ball.playerId === ownerId) {
    focus.x = ball.x;
    focus.z = ball.z;
    focus.active = true;
  }
}

function playBounces(bounces: number[][], now: number) {
  while (nextBounce < bounces.length && bounces[nextBounce][0] <= t) {
    const [, x, z, speed, kind] = bounces[nextBounce++];
    if (now - windowStart > BOUNCE_WINDOW_MS) {
      windowStart = now;
      windowCount = 0;
    }
    if (windowCount++ < MAX_BOUNCE_SOUNDS) playBounce(kind as BounceKind, speed, x, z, 0);
  }
}

function idleClock() {
  const {timeline, viewed} = useStore.getState();
  if (viewed !== null && timeline[viewed]) return timeline[viewed].worldTimeMs / 1000;
  const last = timeline[timeline.length - 1];
  return last ? (last.worldTimeMs + last.durationMs) / 1000 : 0;
}

/** Per frame: advances sink animations and the running entry; `dtMs` is real time. */
export function update(dtMs: number, now: number) {
  step(dtMs, now);
  if (current) clock.t = (current.hit.worldTimeMs + Math.min(t, current.hit.durationMs)) / 1000;
  // Between two queued records the clock holds; the store already shows the final present.
  else if (queue.length === 0) clock.t = idleClock();
}

function step(dtMs: number, now: number) {
  for (let i = 0; i < world.length; i++) {
    const b = world[i];
    if (b.sink > 0 && b.sink < 1) b.sink = Math.min(1, b.sink + dtMs / SINK_MS);
  }
  focus.active = false;
  if (current && now > deadline + MAX_LAG_MS) {
    flush();
    return;
  }
  const item = current ?? begin();
  if (!item) return;
  t += dtMs * item.speed;
  for (let i = 0; i < runs.length; i++) if (!runs[i].done) advance(runs[i], item.hit.playerId);
  playBounces(item.hit.bounces, now);
  if (t < lengthMs) return;
  placeAll(item.hit.end);
  current = null;
  if (queue.length === 0) stop();
}
