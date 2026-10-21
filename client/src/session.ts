const NAME_KEY = 'tg.name';
const PLAYER_KEY = 'tg.playerKey';
const LOBBY_KEY = 'tg.lobbyId';
const PASSWORD_KEY = 'tg.lobbyPassword';

function read(storage: () => Storage, key: string) {
  try {
    return storage().getItem(key);
  } catch {
    return null;
  }
}

function write(storage: () => Storage, key: string, value: string | null) {
  try {
    if (value === null) storage().removeItem(key);
    else storage().setItem(key, value);
  } catch {
    // Without storage the value just lives for this page load.
  }
}

const local = () => localStorage;
const session = () => sessionStorage;

// crypto.randomUUID only exists in secure contexts; plain-http deployments need the fallback.
function uuid() {
  if (crypto.randomUUID) return crypto.randomUUID();
  const b = crypto.getRandomValues(new Uint8Array(16));
  b[6] = (b[6] & 0x0f) | 0x40;
  b[8] = (b[8] & 0x3f) | 0x80;
  const hex = [...b].map((x) => x.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

export const storedName = () => read(local, NAME_KEY) ?? '';

export const rememberName = (name: string) => write(local, NAME_KEY, name);

// Per-tab identity: survives reloads (reconnect restores score and balls), while a second tab is a second player.
export function playerKey() {
  const existing = read(session, PLAYER_KEY);
  if (existing) return existing;
  const key = uuid();
  write(session, PLAYER_KEY, key);
  return key;
}

export type Resumable = {name: string; lobbyId: string; password: string | null};

export function rememberLobby(lobbyId: string | null, password: string | null) {
  write(session, LOBBY_KEY, lobbyId);
  write(session, PASSWORD_KEY, password);
}

export const forgetLobby = () => rememberLobby(null, null);

/** Lobby of a tab that was already in a game, so a reload can skip the menu. */
export function resumable(): Resumable | null {
  const name = storedName().trim();
  const lobbyId = read(session, LOBBY_KEY);
  return name && lobbyId && read(session, PLAYER_KEY) ? {name, lobbyId, password: read(session, PASSWORD_KEY)} : null;
}

/** After quitting, a reload shows the menu and the next join is a new player. */
export function forgetSession() {
  forgetLobby();
  write(session, PLAYER_KEY, null);
}
