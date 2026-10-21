import {useStore} from '../store';
import {roundScore} from './format';

export function Leaderboard() {
  const players = useStore((s) => s.players);
  const me = useStore((s) => s.me);
  const rows = players.filter((p) => p.active && !p.spectator).sort((a, b) => b.points - a.points);
  if (rows.length === 0) return null;

  return (
    <aside className='panel leaderboard'>
      <ol>
        {rows.map((p) => (
          <li key={p.id} className={p.id === me ? 'me' : undefined}>
            <span className='place'>{rows.findIndex((r) => r.points === p.points) + 1}</span>
            <i className='dot' style={{background: p.color}} />
            <span className='name'>{p.name}</span>
            <span className='dim'>{roundScore(p.holed, p.score)}</span>
            <span className='pts'>{p.points}</span>
          </li>
        ))}
      </ol>
    </aside>
  );
}
