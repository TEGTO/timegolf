import {useStore} from '../store';
import {useServerNow} from './useNow';

// Finer than the default tick so the whole-second countdown flips close to on time.
const COUNTDOWN_TICK_MS = 200;

function Countdown({text}: {text: string}) {
  const endsAt = useStore((s) => s.phaseEndsAt);
  const now = useServerNow(COUNTDOWN_TICK_MS);
  return (
    <p className='countdown'>
      {text} <b>{Math.max(0, Math.ceil((endsAt - now) / 1000))}</b>
    </p>
  );
}

export function Results() {
  const phase = useStore((s) => s.phase);
  const round = useStore((s) => s.roundResult);
  const match = useStore((s) => s.matchResult);
  const rounds = useStore((s) => s.rounds);
  const me = useStore((s) => s.me);

  if (phase === 'MatchResults' && match) {
    const winners = new Set(match.winnerIds);
    const names = match.rows.filter((r) => winners.has(r.playerId)).map((r) => r.name);
    return (
      <div className='overlay'>
        <div className='card results'>
          <h2>
            {names.length > 0 ? `${names.join(' & ')} ${names.length > 1 ? 'share the win' : 'wins'}` : 'No winner'}
          </h2>
          <table>
            <tbody>
              {match.rows.map((r) => (
                <tr
                  key={r.playerId}
                  className={`${winners.has(r.playerId) ? 'winner' : ''} ${r.playerId === me ? 'me' : ''}`}
                >
                  <td className='place'>{match.rows.findIndex((o) => o.points === r.points) + 1}</td>
                  <td>
                    <i className='dot' style={{background: r.color}} />
                    {r.name}
                  </td>
                  <td className='num pts'>{r.points}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Countdown text='New match in' />
        </div>
      </div>
    );
  }

  if (phase === 'RoundResults' && round) {
    return (
      <div className='overlay'>
        <div className='card results'>
          <h2>
            Round {round.round} of {rounds}
          </h2>
          <table>
            <thead>
              <tr>
                <th />
                <th>Player</th>
                <th className='num'>Hits</th>
                <th className='num'>Distance</th>
                <th className='num'>Pts</th>
              </tr>
            </thead>
            <tbody>
              {round.rows.map((r) => (
                <tr key={r.playerId} className={r.playerId === me ? 'me' : undefined}>
                  <td className='place'>{round.rows.findIndex((o) => o.score === r.score) + 1}</td>
                  <td>
                    <i className='dot' style={{background: r.color}} />
                    {r.name}
                  </td>
                  <td className='num'>{r.hits}</td>
                  <td className='num'>{r.holed ? 'holed' : r.distance === null ? '—' : r.distance.toFixed(1)}</td>
                  <td className='num pts'>+{r.points}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Countdown text={round.round >= rounds ? 'Final standings in' : 'Next round in'} />
        </div>
      </div>
    );
  }

  return null;
}
