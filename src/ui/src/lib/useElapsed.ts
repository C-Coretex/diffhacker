import { useEffect, useState } from 'react';

/**
 * Milliseconds since `startedAt`, re-rendering once a second while it is set.
 *
 * A tick of its own rather than a value derived from the latest event, because the moment a
 * reviewer most wants to see the clock move is the long silence while the model thinks — which is
 * exactly when no event arrives.
 */
export function useElapsed(startedAt: number | undefined): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (startedAt === undefined) return;

    setNow(Date.now());
    const timer = window.setInterval(() => setNow(Date.now()), 1000);

    return () => window.clearInterval(timer);
  }, [startedAt]);

  return startedAt === undefined ? 0 : Math.max(0, now - startedAt);
}
