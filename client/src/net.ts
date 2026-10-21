import {HubConnectionBuilder, LogLevel, type HubConnection} from '@microsoft/signalr';
import {enqueue, flush} from './playback';
import {syncClock} from './serverClock';
import {forgetLobby, forgetSession, playerKey, rememberLobby, rememberName, resumable} from './session';
import {initSfx} from './sfx';
import {actions, aimMode, isMyTurn, useStore} from './store';
import type {
  ActionRejectedDto,
  AimDto,
  LobbyInfoDto,
  MatchEndedDto,
  PhaseDto,
  PlayerDto,
  RoundEndedDto,
  RoundStartedDto,
  TimelinePlayedDto,
  TurnDto,
  TurnSkippedDto,
  WelcomeDto
} from './types';

const HUB_URL = '/hubs/game';
// Watchers smooth the arrow between updates, so 10/s is plenty and well under the server's 20/s cap.
const AIM_SEND_INTERVAL_MS = 100;

type Seat = {lobbyId: string; password: string | null; name: string};

let conn: HubConnection | null = null;
let started: Promise<HubConnection> | null = null;
/** Lobby this tab sits in; re-joined after every reconnect. */
let seat: Seat | null = null;
/** My latest aim, relayed while it's my turn. */
const myAim = {dirX: 0, dirZ: 0, power: null as number | null};
let aimTimer = 0;
let lastAimSent = 0;

function register(c: HubConnection) {
  c.on('TurnStarted', (t: TurnDto) => actions.turnStarted(t));
  c.on('TurnSkipped', (d: TurnSkippedDto) => actions.turnSkipped(d));
  c.on('TimelinePlayed', (d: TimelinePlayedDto) => {
    enqueue(d.hits, d.replaced);
    actions.timelinePlayed(d);
  });
  c.on('RoundStarted', (r: RoundStartedDto) => actions.roundStarted(r));
  c.on('RoundEnded', (r: RoundEndedDto) => actions.roundEnded(r));
  c.on('MatchEnded', (m: MatchEndedDto) => actions.matchEnded(m));
  c.on('Phase', (p: PhaseDto) => {
    // A restart vote can cut playback short; the ready room shouldn't animate the dead game behind it.
    if (p.phase === 'Lobby') flush();
    actions.phase(p);
  });
  c.on('AimUpdated', (a: AimDto) => actions.aimUpdated(a));
  c.on('Players', (players: PlayerDto[]) => actions.players(players));
  c.on('ActionRejected', (r: ActionRejectedDto) => actions.toast(r.reason));
  c.on('Lobbies', (lobbies: LobbyInfoDto[]) => useStore.setState({lobbies}));
}

/** Server HubException text ("Wrong password"…), or a generic line when the server can't be reached. */
function reason(err: unknown) {
  const message = err instanceof Error ? err.message : '';
  return /HubException: (.+)$/.exec(message)?.[1] ?? 'Could not reach the course';
}

function connection() {
  if (started) return started;
  const c = new HubConnectionBuilder()
    .withUrl(HUB_URL)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
  conn = c;
  register(c);
  c.onreconnecting(() => useStore.setState({connection: 'reconnecting'}));
  c.onreconnected(() => void resync(c));
  c.onclose(() => {
    if (conn !== c) return;
    conn = null;
    started = null;
    useStore.setState({connection: 'offline'});
  });
  useStore.setState({connection: 'connecting'});
  started = c.start().then(
    () => {
      useStore.setState({connection: 'online'});
      return c;
    },
    (err: unknown) => {
      conn = null;
      started = null;
      useStore.setState({connection: 'offline'});
      throw err;
    }
  );
  return started;
}

async function listLobbies(c: HubConnection) {
  useStore.setState({lobbies: await c.invoke<LobbyInfoDto[]>('ListLobbies')});
}

/** Back to the menu, e.g. when the lobby vanished while we were away. */
function kicked(text: string) {
  seat = null;
  forgetLobby();
  flush();
  useStore.setState({joined: false, lobby: null});
  actions.toast(text);
}

// A new connection id is a stranger to the server: re-enter the lobby, or re-subscribe to the menu list.
async function resync(c: HubConnection) {
  useStore.setState({connection: 'online'});
  if (!seat) return listLobbies(c).catch(() => undefined);
  try {
    enter(await c.invoke<WelcomeDto>('Join', seat.lobbyId, seat.password, seat.name, playerKey()));
  } catch (err) {
    kicked(reason(err));
  }
}

function enter(w: WelcomeDto) {
  syncClock(w.serverTime);
  actions.welcome(w);
  useStore.setState({joined: true});
}

/** Connects (if needed) and subscribes to the live lobby list. */
export function openMenu() {
  connection()
    .then(listLobbies)
    .catch(() => undefined);
}

/** Rejects with the server's reason as the message. */
export async function join(lobbyId: string, password: string | null, name: string) {
  initSfx();
  rememberName(name);
  try {
    const c = await connection();
    const w = await c.invoke<WelcomeDto>('Join', lobbyId, password, name, playerKey());
    seat = {lobbyId, password, name};
    rememberLobby(lobbyId, password);
    enter(w);
  } catch (err) {
    throw new Error(reason(err));
  }
}

export async function createLobby(lobbyName: string, password: string | null, maxPlayers: number, name: string) {
  let id: string;
  try {
    id = await (await connection()).invoke<string>('CreateLobby', lobbyName, password, maxPlayers);
  } catch (err) {
    throw new Error(reason(err));
  }
  await join(id, password, name);
}

/** Started at load so a reload goes straight back into the lobby instead of the menu. */
export const resuming: Promise<void> | null = (() => {
  const saved = resumable();
  return saved
    ? join(saved.lobbyId, saved.password, saved.name).catch((err: Error) => {
        forgetLobby();
        actions.toast(err.message);
      })
    : null;
})();

/** Quit: leaves the lobby for good; a reload then shows the menu and the next join is a new player. */
export function leave() {
  seat = null;
  forgetSession();
  flush();
  conn?.invoke('Leave').catch(() => undefined);
  useStore.setState({joined: false, lobby: null, lastMatch: null});
}

/** Manual retry once automatic reconnect has given up. */
export function reconnect() {
  connection()
    .then(resync)
    .catch(() => undefined);
}

function send(method: string, ...args: unknown[]) {
  conn?.invoke(method, ...args).catch(() => actions.toast('Action failed to send'));
}

export function setReady(ready: boolean) {
  send('SetReady', ready);
}

/** Present hit, or Replace when re-doing a viewed own record. */
export function shoot(dirX: number, dirZ: number, power: number) {
  const mode = aimMode(useStore.getState());
  if (mode?.kind === 'hit') send('Hit', dirX, dirZ, power);
  else if (mode?.kind === 'replace') send('Replace', mode.record.index, dirX, dirZ, power);
}

function sendAim() {
  clearTimeout(aimTimer);
  aimTimer = 0;
  lastAimSent = performance.now();
  const s = useStore.getState();
  // Transient: a lost update is replaced by the next one, so no toast.
  if (isMyTurn(s)) conn?.invoke('Aim', s.viewed, myAim.dirX, myAim.dirZ, myAim.power).catch(() => undefined);
}

/** Live aim for the watchers (§10.2): throttled while dragging; `power: null` (stopped aiming) goes out at once. */
export function aim(dirX: number, dirZ: number, power: number | null) {
  myAim.dirX = dirX;
  myAim.dirZ = dirZ;
  myAim.power = power;
  if (power === null) return sendAim();
  if (aimTimer) return;
  const wait = lastAimSent + AIM_SEND_INTERVAL_MS - performance.now();
  if (wait <= 0) sendAim();
  else aimTimer = window.setTimeout(sendAim, wait);
}

useStore.subscribe((s, prev) => {
  if (isMyTurn(s) && (s.viewed !== prev.viewed || !isMyTurn(prev))) sendAim();
});

export function voteRestart() {
  send('VoteRestart');
}

export function skip() {
  if (isMyTurn(useStore.getState())) send('Skip');
}
