// Later samples only nudge the offset, so countdowns don't jump on a rejoin.
const SMOOTHING = 0.1;

let offset = 0;
let synced = false;

export function syncClock(serverTime: number) {
  const sample = serverTime - Date.now();
  offset = synced ? offset + (sample - offset) * SMOOTHING : sample;
  synced = true;
}

export const serverNow = () => Date.now() + offset;
