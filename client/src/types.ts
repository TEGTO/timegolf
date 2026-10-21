// Mirrors server/TimeGolf/Dtos.cs (SignalR JSON, camelCase).

export type Phase = 'Lobby' | 'Playing' | 'RoundResults' | 'MatchResults';

export type Rect = {x: number; z: number; w: number; d: number};

export type LevelDto = {
  seed: number;
  round: number;
  width: number;
  height: number;
  cells: string;
  heights: number[];
  walls: [number, number, number, number][];
  pillars: Rect[];
  bumpers: {x: number; z: number; r: number}[];
  windmills: WindmillDto[];
  start: Rect;
  spawns: [number, number][];
  hole: {x: number; z: number};
  palette: number;
};

/** (x, z) = tunnel center; axis 0 = ball travels along x, 1 = along z. Blade angle = phase + windmillOmega · world time. */
export type WindmillDto = {x: number; z: number; axis: 0 | 1; phase: number};

export type BallPosDto = {playerId: string; x: number; z: number; holed: boolean};

/** points: flat [x0, z0, x1, z1, …] at 20 Hz from hit start; the last point is rest or hole. */
export type TrackDto = {playerId: string; points: number[]};

/** 0 wall, 1 ball, 2 bumper. */
export type BounceKind = 0 | 1 | 2;

export type HitPlayDto = {
  index: number;
  playerId: string;
  dirX: number;
  dirZ: number;
  power: number;
  void: boolean;
  /** Checkpoint `index`: all balls right before this hit. */
  start: BallPosDto[];
  tracks: TrackDto[];
  /** [tMs, x, z, speed, kind] */
  bounces: number[][];
  end: BallPosDto[];
  durationMs: number;
  /** World clock at record start (windmill angle source). */
  worldTimeMs: number;
};

export type TurnDto = {turn: number; turns: number; playerId: string; endsAt: number};

export type PlayerDto = {
  id: string;
  name: string;
  color: string;
  points: number;
  hits: number;
  holed: boolean;
  distance: number | null;
  score: number | null;
  active: boolean;
  spectator: boolean;
  restartVote: boolean;
  ready: boolean;
};

export type ConfigDto = {
  ballRadius: number;
  holeRadius: number;
  maxHitSpeed: number;
  replaySpeed: number;
  playbackGapMs: number;
  /** rad/s */
  windmillOmega: number;
  /** rad */
  windmillBladeHalfAngle: number;
};

export type LobbyInfoDto = {
  id: string;
  name: string;
  locked: boolean;
  /** Active players. */
  players: number;
  maxPlayers: number;
  phase: Phase;
};

export type WelcomeDto = {
  lobby: LobbyInfoDto;
  lastMatch: MatchEndedDto | null;
  playerId: string;
  serverTime: number;
  phase: Phase;
  phaseEndsAt: number;
  round: number;
  rounds: number;
  level: LevelDto | null;
  order: string[];
  balls: BallPosDto[];
  timeline: HitPlayDto[];
  turn: TurnDto | null;
  players: PlayerDto[];
  config: ConfigDto;
};

export type TurnSkippedDto = {turn: number; playerId: string; reason: 'skip' | 'timeout' | 'inactive'};

export type TimelinePlayedDto = {fromIndex: number; replaced: boolean; hits: HitPlayDto[]};

export type RoundStartedDto = {
  round: number;
  rounds: number;
  level: LevelDto;
  order: string[];
  balls: BallPosDto[];
  players: PlayerDto[];
};

export type RoundRowDto = {
  playerId: string;
  name: string;
  color: string;
  hits: number;
  holed: boolean;
  distance: number | null;
  score: number;
  points: number;
};

export type RoundEndedDto = {round: number; rows: RoundRowDto[]};

export type MatchRowDto = {playerId: string; name: string; color: string; points: number};

export type MatchEndedDto = {rows: MatchRowDto[]; winnerIds: string[]};

export type PhaseDto = {phase: Phase; phaseEndsAt: number; round: number; rounds: number};

export type ActionRejectedDto = {reason: string};

/** Live aim of the current-turn player. viewIndex: viewed record, null = present; power: null when not aiming. */
export type AimDto = {playerId: string; viewIndex: number | null; dirX: number; dirZ: number; power: number | null};
