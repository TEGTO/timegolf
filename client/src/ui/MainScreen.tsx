import {useEffect, useRef, useState} from 'react';
import {openMenu} from '../net';
import {storedName} from '../session';
import {useStore} from '../store';
import {CreateLobbyForm} from './CreateLobbyForm';
import {LobbyRow} from './LobbyRow';
import {Scenery} from './Scenery';
import {Toast} from './Toast';

const NAME_MAX = 16;

export function MainScreen() {
  const [name, setName] = useState(storedName);
  const [busy, setBusy] = useState(false);
  const [creating, setCreating] = useState(false);
  const [nudge, setNudge] = useState(false);
  const nameInput = useRef<HTMLInputElement>(null);
  const lobbies = useStore((s) => s.lobbies);
  const offline = useStore((s) => s.connection === 'offline');

  useEffect(openMenu, []);

  const player = () => {
    const trimmed = name.trim();
    if (trimmed) return trimmed;
    nameInput.current?.focus();
    setNudge(true);
    return null;
  };

  const run = async (task: () => Promise<void>) => {
    setBusy(true);
    try {
      await task();
      return null;
    } catch (err) {
      return err instanceof Error ? err.message : 'Something went wrong';
    } finally {
      setBusy(false);
    }
  };

  const props = {player, busy, run};

  return (
    <main className='menu sky'>
      <Scenery />
      <div className='menu-form'>
        <h1 className='logo'>TimeGolf</h1>
        <input
          ref={nameInput}
          className={`name-input${nudge ? ' nudge' : ''}`}
          onAnimationEnd={() => setNudge(false)}
          autoFocus={!name}
          maxLength={NAME_MAX}
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder='Your name'
          aria-label='Your name'
          spellCheck={false}
        />
        <section className='lobbies'>
          {lobbies.length > 0 && (
            <ul className='lobby-list'>
              {lobbies.map((l) => (
                <LobbyRow key={l.id} lobby={l} {...props} />
              ))}
            </ul>
          )}
          {creating ? (
            <CreateLobbyForm {...props} playerName={name.trim()} close={() => setCreating(false)} />
          ) : (
            <button className='play create' onClick={() => setCreating(true)}>
              Create
            </button>
          )}
          {offline && (
            <p className='error'>
              Offline
              <button onClick={openMenu}>Retry</button>
            </p>
          )}
        </section>
      </div>
      <Toast />
    </main>
  );
}
