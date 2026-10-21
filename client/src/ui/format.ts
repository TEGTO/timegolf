export function clock(ms: number) {
  const total = Math.max(0, Math.ceil(ms / 1000));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}

/** Live round points, flagged when already holed, e.g. `⛳ 50` or `16`. */
export function roundScore(holed: boolean, score: number | null) {
  if (score === null) return '—';
  return holed ? `⛳ ${score}` : `${score}`;
}
