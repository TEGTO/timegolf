import {create} from 'zustand';
import {isMuted, play, setMuted} from './sfx';
import type {
  AimDto,
  BallPosDto,
  ConfigDto,
  HitPlayDto,
  LevelDto,
  LobbyInfoDto,
  MatchEndedDto,
  Phase,
  PhaseDto,
  PlayerDto,
  RoundEndedDto,
  RoundStartedDto,
  TimelinePlayedDto,
  TurnDto,
  TurnSkippedDto,
  WelcomeDto
} from './types';

export type Connection = 'connecting' | 'online' | 'reconnecting' | 'offline';

export type State = {
  joined: boolean;
  connection: Connection;
  /** Main-menu list, pushed live while not in a lobby. */
  lobbies: LobbyInfoDto[];
  lobby: LobbyInfoDto | null;
  /** Shown in the ready room after a match. */
  lastMatch: MatchEndedDto | null;
  me: string | null;
  config: ConfigDto;
  phase: Phase;
  phaseEndsAt: number;
  round: number;
  rounds: number;
  level: LevelDto | null;
  order: string[];
  turn: TurnDto | null;
  timeline: HitPlayDto[];
  /** Present state (after the last record). */
  balls: BallPosDto[];
  players: PlayerDto[];
  /** Viewed checkpoint index; null = present. */
  viewed: number | null;
  playing: boolean;
  /** What the running playback shows: the first queued record and whether it was a replace. */
  shot: {playerId: string; index: number; replaced: boolean} | null;
  /** Set when a replace moved my ball differently; cleared by the next timeline change or checkpoint view. */
  changed: Changed | null;
  /** Hit index of the hovered timeline marker. */
  hovered: number | null;
  /** Live aim of the current-turn player when that isn't me (§10.2). */
  watched: AimDto | null;
  roundResult: RoundEndedDto | null;
  matchResult: MatchEndedDto | null;
  toast: {text: string; key: number} | null;
  muted: boolean;
};

/** `index`/`playerId`: the replaced hit; `at`: first record where my ball's path differs; `previous`: old records from `at` on. */
export type Changed = {index: number; playerId: string; at: number; previous: HitPlayDto[]};

export type AimMode = {kind: 'hit'} | {kind: 'replace'; record: HitPlayDto} | null;

const SWITCH_SOUND_MS = 120;
// Track points are flat (x, z) pairs; a track that moves needs at least two of them.
const MIN_TRACK_VALUES = 4;
// Re-simulating an unchanged path may differ by float noise; that isn't a divergence.
const SAME_PATH_EPSILON = 1e-3;

/** Placeholders until Welcome delivers the server's config. */
const DEFAULT_CONFIG: ConfigDto = {
  ballRadius: 0.14,
  holeRadius: 0.22,
  maxHitSpeed: 14,
  replaySpeed: 3,
  playbackGapMs: 400,
  windmillOmega: 1.6,
  windmillBladeHalfAngle: (18 * Math.PI) / 180
};

let lastSwitchSound = 0;

export const useStore = create<State>(() => ({
  joined: false,
  connection: 'offline',
  lobbies: [],
  lobby: null,
  lastMatch: null,
  me: null,
  config: DEFAULT_CONFIG,
  phase: 'Lobby',
  phaseEndsAt: 0,
  round: 0,
  rounds: 3,
  level: null,
  order: [],
  turn: null,
  timeline: [],
  balls: [],
  players: [],
  viewed: null,
  playing: false,
  shot: null,
  changed: null,
  hovered: null,
  watched: null,
  roundResult: null,
  matchResult: null,
  toast: null,
  muted: isMuted()
}));

const set = useStore.setState;
const get = useStore.getState;

export const playerOf = (s: State, id: string | null) => s.players.find((p) => p.id === id);

export const trackOf = (hit: HitPlayDto | undefined, id: string) =>
  hit?.tracks.find((t) => t.playerId === id && t.points.length >= MIN_TRACK_VALUES)?.points;

function samePath(a: number[] | undefined, b: number[] | undefined) {
  if (!a || !b) return a === b;
  return a.length === b.length && a.every((v, i) => Math.abs(v - b[i]) < SAME_PATH_EPSILON);
}

/** First record from `from` on where my ball moves differently, or -1. */
function divergence(old: HitPlayDto[], next: HitPlayDto[], from: number, me: string) {
  for (let k = from; k < Math.max(old.length, next.length); k++)
    if (!samePath(trackOf(old[k], me), trackOf(next[k], me))) return k;
  return -1;
}

export const isSpectator = (s: State) => !!playerOf(s, s.me)?.spectator;

export const isMyTurn = (s: State) => s.phase === 'Playing' && !s.playing && s.turn?.playerId === s.me;

export function aimMode(s: State): AimMode {
  if (!isMyTurn(s)) return null;
  if (s.viewed === null) return s.balls.some((b) => b.playerId === s.me && !b.holed) ? {kind: 'hit'} : null;
  const record = s.timeline[s.viewed];
  return record && record.playerId === s.me && !record.void ? {kind: 'replace', record} : null;
}

function switchSound() {
  const now = performance.now();
  if (now - lastSwitchSound < SWITCH_SOUND_MS) return;
  lastSwitchSound = now;
  play('switch');
}

export const actions = {
  welcome(w: WelcomeDto) {
    set({
      lobby: w.lobby,
      lastMatch: w.lastMatch,
      me: w.playerId,
      config: w.config,
      phase: w.phase,
      phaseEndsAt: w.phaseEndsAt,
      round: w.round,
      rounds: w.rounds,
      level: w.level,
      order: w.order,
      turn: w.turn,
      timeline: w.timeline,
      balls: w.balls,
      players: w.players,
      viewed: null,
      changed: null,
      hovered: null,
      watched: null,
      roundResult: null,
      matchResult: null,
      connection: 'online'
    });
  },

  // Re-sent mid-turn (same round) when a player joins before the first hit; no new TurnStarted follows then.
  roundStarted(r: RoundStartedDto) {
    set((s) => ({
      phase: 'Playing',
      round: r.round,
      rounds: r.rounds,
      level: r.level,
      order: r.order,
      balls: r.balls,
      players: r.players,
      timeline: [],
      turn: r.round === s.round ? s.turn : null,
      watched: r.round === s.round ? s.watched : null,
      viewed: null,
      changed: null,
      hovered: null,
      roundResult: null,
      matchResult: null
    }));
  },

  turnStarted(turn: TurnDto) {
    set({turn, watched: null});
    if (turn.playerId === get().me) play('switch');
  },

  turnSkipped(d: TurnSkippedDto) {
    set({turn: null, watched: null});
    if (d.reason !== 'timeout') return;
    const s = get();
    actions.toast(
      d.playerId === s.me ? 'You ran out of time' : `${playerOf(s, d.playerId)?.name ?? 'Player'} ran out of time`
    );
  },

  timelinePlayed(d: TimelinePlayedDto) {
    set((s) => {
      const timeline = s.timeline.slice(0, d.fromIndex).concat(d.hits);
      const by = d.hits[0];
      const at = d.replaced && by && s.me ? divergence(s.timeline, timeline, d.fromIndex, s.me) : -1;
      const changed = at < 0 ? null : {index: d.fromIndex, playerId: by.playerId, at, previous: s.timeline.slice(at)};
      return {
        timeline,
        balls: timeline[timeline.length - 1]?.end ?? s.balls,
        turn: null,
        watched: null,
        viewed: null,
        changed,
        hovered: null
      };
    });
  },

  phase(p: PhaseDto) {
    const clear = p.phase === 'Playing' || p.phase === 'Lobby';
    set((s) => ({
      phase: p.phase,
      phaseEndsAt: p.phaseEndsAt,
      round: p.round,
      rounds: p.rounds,
      turn: p.phase === 'Playing' ? s.turn : null,
      roundResult: clear ? null : s.roundResult,
      matchResult: clear ? null : s.matchResult
    }));
  },

  // A relay still in flight when the turn moved on must not resurrect a stale arrow.
  aimUpdated(a: AimDto) {
    const s = get();
    if (!s.playing && a.playerId !== s.me && a.playerId === s.turn?.playerId) set({watched: a});
  },

  players(players: PlayerDto[]) {
    set({players});
  },

  view(index: number | null) {
    const s = get();
    if (s.playing) return;
    const next = index === null || index >= s.timeline.length ? null : Math.max(0, index);
    if (next === s.viewed) return;
    switchSound();
    set({viewed: next});
  },

  hover(index: number | null) {
    set({hovered: index});
  },

  /** ←/→: walks checkpoints; stepping past the last record returns to present. */
  step(delta: -1 | 1) {
    const {viewed, timeline} = get();
    if (timeline.length === 0) return;
    actions.view((viewed ?? timeline.length) + delta);
  },

  roundEnded(r: RoundEndedDto) {
    set({roundResult: r});
  },

  matchEnded(m: MatchEndedDto) {
    set({matchResult: m, lastMatch: m});
  },

  toast(text: string) {
    set({toast: {text, key: Date.now()}});
  },

  toggleMute() {
    setMuted(!get().muted);
    set({muted: !get().muted});
  }
};
