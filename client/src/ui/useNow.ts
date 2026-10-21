import {useEffect, useState} from 'react';
import {serverNow} from '../serverClock';

const DEFAULT_TICK_MS = 250;

/** Server-clock "now", refreshed on an interval for countdowns. */
export function useServerNow(intervalMs = DEFAULT_TICK_MS) {
  const [now, setNow] = useState(serverNow);
  useEffect(() => {
    const id = setInterval(() => setNow(serverNow()), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs]);
  return now;
}
