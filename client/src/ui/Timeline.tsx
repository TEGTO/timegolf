import {UNKNOWN_PLAYER_COLOR} from '../constants';
import {actions, aimMode, isMyTurn, useStore} from '../store';

export function Timeline() {
  const timeline = useStore((s) => s.timeline);
  const viewed = useStore((s) => s.viewed);
  const players = useStore((s) => s.players);
  const me = useStore((s) => s.me);
  const myTurn = useStore(isMyTurn);
  const playing = useStore((s) => s.playing);
  const phase = useStore((s) => s.phase);
  const watched = useStore((s) => s.watched);
  const redo = useStore((s) => {
    const mode = aimMode(s);
    return mode?.kind === 'replace' ? mode.record.index + 1 : null;
  });
  if (phase !== 'Playing') return null;
  const watchedAt = watched && (timeline[watched.viewIndex ?? -1] ? watched.viewIndex : null);
  const watchDot = watched && (
    <span
      className='tl-watch'
      style={{background: players.find((p) => p.id === watched.playerId)?.color ?? UNKNOWN_PLAYER_COLOR}}
    />
  );

  return (
    <section className='timeline panel'>
      {timeline.map((h, i) => {
        const owner = players.find((p) => p.id === h.playerId);
        const classes = ['tl-marker'];
        if (h.void) classes.push('void');
        if (i === viewed) classes.push('viewed');
        if (myTurn && h.playerId === me && !h.void) classes.push('redo');
        return (
          <button
            key={i}
            className={classes.join(' ')}
            style={{background: owner?.color ?? UNKNOWN_PLAYER_COLOR}}
            disabled={playing}
            title={`#${i + 1} · ${owner?.name ?? '?'}`}
            onClick={() => actions.view(i)}
            onMouseEnter={() => actions.hover(i)}
            onMouseLeave={() => actions.hover(null)}
          >
            {i + 1}
            {watchedAt === i && watchDot}
          </button>
        );
      })}
      <span className='tl-end'>
        {redo !== null && <span className='tl-redo'>Re-do #{redo}</span>}
        <button
          className={`tl-now${viewed === null ? ' viewed' : ''}`}
          disabled={playing}
          onClick={() => actions.view(null)}
        >
          Now
          {watchedAt === null && watchDot}
        </button>
      </span>
    </section>
  );
}
