import {leave, setReady} from '../net';
import {playerOf, useStore} from '../store';

function LastMatch() {
  const match = useStore((s) => s.lastMatch);
  const me = useStore((s) => s.me);
  if (!match || match.rows.length === 0) return null;
  const winners = new Set(match.winnerIds);
  return (
    <section className='last-match'>
      <h3>Last match</h3>
      <ol>
        {match.rows.map((r) => (
          <li
            key={r.playerId}
            className={`${winners.has(r.playerId) ? 'winner' : ''} ${r.playerId === me ? 'me' : ''}`}
          >
            <span className='place'>{match.rows.findIndex((o) => o.points === r.points) + 1}</span>
            <i className='dot' style={{background: r.color}} />
            <span className='name'>{r.name}</span>
            <span className='pts'>{r.points}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}

export function ReadyRoom() {
  const lobby = useStore((s) => s.lobby);
  const players = useStore((s) => s.players);
  const me = useStore((s) => s.me);
  const ready = useStore((s) => !!playerOf(s, s.me)?.ready);
  const active = players.filter((p) => p.active);
  const count = active.filter((p) => p.ready).length;

  return (
    <div className='overlay lobby-room'>
      <div className='card room'>
        <header>
          <h2>{lobby?.name}</h2>
          <span className='dim'>
            {count}/{active.length} ready
          </span>
        </header>
        <ul className='room-players'>
          {active.map((p) => (
            <li key={p.id} className={p.id === me ? 'me' : undefined}>
              <i className='dot' style={{background: p.color}} />
              <span className='name'>{p.name}</span>
              <span className={`tick${p.ready ? ' on' : ''}`} aria-label={p.ready ? 'Ready' : 'Not ready'}>
                <svg viewBox='0 0 16 16' width='14' height='14'>
                  <path
                    d='M3.5 8.5l3 3 6-7'
                    fill='none'
                    stroke='currentColor'
                    strokeWidth='2.4'
                    strokeLinecap='round'
                  />
                </svg>
              </span>
            </li>
          ))}
        </ul>
        <LastMatch />
        <button className={`ready${ready ? ' on' : ''}`} onClick={() => setReady(!ready)} aria-pressed={ready}>
          {ready ? 'Ready!' : 'Ready'}
        </button>
        <button className='chip quit' onClick={leave}>
          Quit
        </button>
      </div>
    </div>
  );
}
