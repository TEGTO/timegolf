import type {BounceKind} from './types';

export type Sfx = 'hit' | 'hole' | 'switch' | 'wall';

const names: Sfx[] = ['hit', 'hole', 'switch', 'wall'];
const MUTE_KEY = 'tg.muted';
/** Distance (world units) over which a positional sound fades down to `MIN_VOLUME`. */
const FALLOFF_UNITS = 18;
// Far sounds stay faintly audible so off-screen action isn't silent.
const MIN_VOLUME = 0.12;
// exponentialRamp can't reach 0; this is inaudible.
const SILENT_GAIN = 0.001;
const BOUNCE_MIN_LOUDNESS = 0.12;
/** Extra bounce loudness per u/s of impact speed. */
const BOUNCE_LOUDNESS_PER_SPEED = 1 / 9;

// Fallback blips: [wave, startHz, endHz, seconds, gain]
const synth: Record<Sfx, [OscillatorType, number, number, number, number]> = {
  hit: ['triangle', 520, 180, 0.09, 0.5],
  hole: ['sine', 660, 1320, 0.32, 0.4],
  switch: ['square', 900, 1100, 0.04, 0.08],
  wall: ['triangle', 160, 90, 0.06, 0.35]
};

// Per bounce kind (wall, ball, bumper): [playback rate, loudness].
const bounceVoice: Record<BounceKind, [number, number]> = {0: [1, 1], 1: [1.7, 0.7], 2: [0.75, 1.2]};

let ctx: AudioContext | null = null;
const buffers = new Map<Sfx, AudioBuffer>();
let muted = readMuted();

/** Listener position on the ground plane, kept in sync with the camera target by the scene. */
export const listener = {x: 0, z: 0};

function readMuted() {
  try {
    return localStorage.getItem(MUTE_KEY) === '1';
  } catch {
    return false;
  }
}

export function isMuted() {
  return muted;
}

export function setMuted(value: boolean) {
  muted = value;
  try {
    localStorage.setItem(MUTE_KEY, value ? '1' : '0');
  } catch {
    // Private mode: mute still works for this session.
  }
}

/** Must run inside a user gesture, otherwise browsers keep the context suspended. */
export function initSfx() {
  if (ctx) return;
  ctx = new AudioContext();
  const audio = ctx;
  for (const name of names) {
    fetch(`/sfx/${name}.mp3`)
      .then((r) => (r.ok ? r.arrayBuffer() : Promise.reject()))
      .then((data) => audio.decodeAudioData(data))
      .then((buf) => buffers.set(name, buf))
      .catch(() => undefined);
  }
}

export function play(name: Sfx, x?: number, z?: number, level = 1, rate = 1, delay = 0) {
  if (muted || !ctx) return;
  if (ctx.state === 'suspended') void ctx.resume();
  const distance = x === undefined || z === undefined ? 0 : Math.hypot(x - listener.x, z - listener.z);
  const volume = Math.max(MIN_VOLUME, 1 - distance / FALLOFF_UNITS) * level;
  const at = ctx.currentTime + delay;
  const gain = ctx.createGain();
  gain.connect(ctx.destination);
  const buffer = buffers.get(name);
  if (buffer) {
    const src = ctx.createBufferSource();
    src.buffer = buffer;
    src.playbackRate.value = rate;
    gain.gain.value = volume;
    src.connect(gain);
    src.start(at);
    return;
  }
  const [wave, from, to, seconds, peak] = synth[name];
  const osc = ctx.createOscillator();
  osc.type = wave;
  osc.frequency.setValueAtTime(from * rate, at);
  osc.frequency.exponentialRampToValueAtTime(to * rate, at + seconds);
  gain.gain.setValueAtTime(peak * volume, at);
  gain.gain.exponentialRampToValueAtTime(SILENT_GAIN, at + seconds);
  osc.connect(gain);
  osc.start(at);
  osc.stop(at + seconds);
}

/** Collision click; impact speed (0.4..14 u/s) sets loudness. */
export function playBounce(kind: BounceKind, impact: number, x: number, z: number, delay: number) {
  const [rate, loudness] = bounceVoice[kind] ?? bounceVoice[0];
  play('wall', x, z, loudness * Math.min(1, BOUNCE_MIN_LOUDNESS + impact * BOUNCE_LOUDNESS_PER_SPEED), rate, delay);
}
