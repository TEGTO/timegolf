import {useShallow} from 'zustand/react/shallow';
import {leave, reconnect, skip, voteRestart} from '../net';
import {actions, isMyTurn, isSpectator, playerOf, useStore} from '../store';
import {clock} from './format';
import {useServerNow} from './useNow';

function Speaker({muted}: {muted: boolean}) {
  return (
    <svg viewBox='0 0 24 24' width='16' height='16' fill='none' stroke='currentColor' strokeWidth='2'>
      <path d='M4 9h4l5-4v14l-5-4H4z' fill='currentColor' stroke='none' />
      {muted ? <path d='M16 9l5 6M21 9l-5 6' /> : <path d='M16 8.5a5 5 0 0 1 0 7M18.5 6a8.5 8.5 0 0 1 0 12' />}
    </svg>
  );
}

function Status() {
  const connection = useStore((s) => s.connection);
  if (connection === 'online' || connection === 'connecting') return null;
  if (connection === 'reconnecting') return <div className='status'>Reconnecting…</div>;
  return (
    <div className='status offline'>
      Disconnected
      <button onClick={reconnect}>Reconnect</button>
    </div>
  );
}

function TurnInfo() {
  const turn = useStore((s) => s.turn);
  const shot = useStore((s) => s.shot);
  const mine = useStore(isMyTurn);
  const name = useStore((s) => playerOf(s, turn?.playerId ?? null)?.name ?? '—');
  const shooter = useStore((s) => playerOf(s, s.shot?.playerId ?? null)?.name ?? 'Player');
  const now = useServerNow();
  if (shot)
    return (
      <span className='shot'>
        {shot.replaced ? ` · ${shooter} changed hit #${shot.index + 1}` : ` · ${shooter}'s shot`}
      </span>
    );
  if (!turn) return null;
  return (
    <>
      {` · Turn ${turn.turn}/${turn.turns} · `}
      {mine ? <span className='mine'>Your turn</span> : name}
      {' · '}
      <span className='clock'>{clock(turn.endsAt - now)}</span>
    </>
  );
}

function Restart() {
  const [votes, needed, mine] = useStore(
    useShallow((s) => {
      const active = s.players.filter((p) => p.active);
      return [
        active.filter((p) => p.restartVote).length,
        Math.floor(active.length / 2) + 1,
        !!playerOf(s, s.me)?.restartVote
      ];
    })
  );
  return (
    <button className={`chip${mine ? ' on' : ''}`} onClick={voteRestart} aria-pressed={mine}>
      Restart {votes}/{needed}
    </button>
  );
}

export function Hud() {
  const phase = useStore((s) => s.phase);
  const round = useStore((s) => s.round);
  const rounds = useStore((s) => s.rounds);
  const muted = useStore((s) => s.muted);
  const mine = useStore(isMyTurn);
  const spectator = useStore((s) => phase === 'Playing' && isSpectator(s));

  if (phase === 'Lobby') return <Status />;

  let body;
  if (phase === 'MatchResults') body = 'Match over';
  else if (phase === 'RoundResults') body = `Round ${round}/${rounds}`;
  else
    body = (
      <>
        Round {round}/{rounds}
        <TurnInfo />
      </>
    );

  return (
    <>
      <div className='panel hud'>
        <span>{body}</span>
        {mine && (
          <button className='skip' onClick={skip}>
            Skip
          </button>
        )}
      </div>
      <div className='panel controls'>
        <button className='icon' onClick={actions.toggleMute} aria-label={muted ? 'Unmute' : 'Mute'}>
          <Speaker muted={muted} />
        </button>
        <Restart />
        <button className='chip' onClick={leave}>
          Quit
        </button>
      </div>
      {spectator && <div className='banner'>Spectating until next round</div>}
      <Status />
    </>
  );
}
