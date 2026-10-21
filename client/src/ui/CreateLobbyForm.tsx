import {useState, type FormEvent} from 'react';
import {createLobby} from '../net';
import type {MenuProps} from './LobbyRow';
import {useFlash} from './useFlash';

const LOBBY_NAME_MAX = 24;
const MIN_PLAYERS = 1;
// Mirrors the server's MaxLobbyPlayers.
const MAX_PLAYERS = 16;
const DEFAULT_MAX_PLAYERS = 8;

export function CreateLobbyForm({
  player,
  busy,
  run,
  close,
  playerName
}: MenuProps & {close: () => void; playerName: string}) {
  const [name, setName] = useState('');
  const [password, setPassword] = useState('');
  const [max, setMax] = useState(DEFAULT_MAX_PLAYERS);
  const [error, setError] = useFlash();
  const fallback = playerName ? `${playerName}'s game` : 'Lobby name';

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const me = player();
    if (!me || busy) return;
    setError(await run(() => createLobby(name.trim() || `${me}'s game`, password || null, max, me)));
  };

  return (
    <form className='create-form' onSubmit={submit} onKeyDown={(e) => e.key === 'Escape' && close()}>
      <input
        autoFocus
        maxLength={LOBBY_NAME_MAX}
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder={fallback}
        aria-label='Lobby name'
        spellCheck={false}
      />
      <input
        type='password'
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        placeholder='Password (optional)'
        aria-label='Password (optional)'
      />
      <div className='create-row'>
        <div className='stepper' aria-label='Max players'>
          <button
            type='button'
            onClick={() => setMax(Math.max(MIN_PLAYERS, max - 1))}
            disabled={max <= MIN_PLAYERS}
            aria-label='Fewer'
          >
            −
          </button>
          <span>
            <b>{max}</b> players
          </span>
          <button
            type='button'
            onClick={() => setMax(Math.min(MAX_PLAYERS, max + 1))}
            disabled={max >= MAX_PLAYERS}
            aria-label='More'
          >
            +
          </button>
        </div>
        <button type='button' className='chip' onClick={close}>
          Cancel
        </button>
        <button className='join' disabled={busy}>
          Create
        </button>
      </div>
      {error && <p className='lobby-error'>{error}</p>}
    </form>
  );
}
