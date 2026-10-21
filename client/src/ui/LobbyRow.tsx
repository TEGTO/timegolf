import {useState, type FormEvent} from 'react';
import {join} from '../net';
import type {LobbyInfoDto} from '../types';
import {useFlash} from './useFlash';

/** Shared by the lobby rows and the create form. */
export type MenuProps = {
  /** Player name, or null (after nudging the name field) when it's still empty. */
  player: () => string | null;
  busy: boolean;
  run: (task: () => Promise<void>) => Promise<string | null>;
};

function Lock() {
  return (
    <svg className='lock' viewBox='0 0 16 16' width='13' height='13' aria-label='Password'>
      <rect x='3' y='7' width='10' height='7.5' rx='2' fill='currentColor' />
      <path d='M5.5 7V5a2.5 2.5 0 0 1 5 0v2' fill='none' stroke='currentColor' strokeWidth='1.8' />
    </svg>
  );
}

export function LobbyRow({lobby, player, busy, run}: MenuProps & {lobby: LobbyInfoDto}) {
  const [asking, setAsking] = useState(false);
  const [password, setPassword] = useState('');
  const [error, setError] = useFlash();
  const full = lobby.players >= lobby.maxPlayers;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const name = player();
    if (!name || busy) return;
    if (lobby.locked && !asking) return setAsking(true);
    setError(await run(() => join(lobby.id, lobby.locked ? password : null, name)));
  };

  return (
    <li>
      <form className='lobby' onSubmit={submit}>
        <span className='lobby-name'>
          {lobby.name}
          {lobby.locked && <Lock />}
        </span>
        {error ? (
          <span className='lobby-error'>{error}</span>
        ) : (
          <span className='lobby-meta'>
            <span className={lobby.phase === 'Lobby' ? 'waiting' : 'in-game'}>
              {lobby.phase === 'Lobby' ? 'waiting' : 'in game'}
            </span>
            <span className='count'>
              {lobby.players}/{lobby.maxPlayers}
            </span>
          </span>
        )}
        {asking && (
          <input
            autoFocus
            type='password'
            className='lobby-password'
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            onKeyDown={(e) => e.key === 'Escape' && setAsking(false)}
            placeholder='Password'
            aria-label='Lobby password'
          />
        )}
        <button className='join' disabled={busy || full}>
          {full ? 'Full' : 'Join'}
        </button>
      </form>
    </li>
  );
}
