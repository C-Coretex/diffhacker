import { useCallback, useEffect, useRef } from 'react';
import { checkAnalysisFreshness } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';

/**
 * The shortest gap between two checks brought on by the window coming back into focus. Short,
 * because a reviewer who edits a file in their editor and alt-tabs back wants the prompt then, not
 * in half a minute; there at all, so flicking between windows does not queue a changeset read per
 * flick.
 */
export const FRESHNESS_THROTTLE_MS = 2_000;

/**
 * Requirement 9: asks the host whether the working tree has moved since the analysis on screen ran.
 *
 * Asked **after** the analysis is drawn — when it is opened, reopened or produced — and again
 * whenever the window regains focus, which is the moment a reviewer comes back from their editor.
 * Never before: reading and hashing a large changeset takes a moment, and a reopened analysis is
 * supposed to be instant (requirement 8).
 *
 * At most one check is in flight per analysis. A check that fails is logged and forgotten; it must
 * never take the analysis off the screen or claim it is stale when nobody knows.
 */
export function useFreshnessCheck(
  repositoryPath: string | undefined,
  analysisId: string | undefined,
  running: boolean,
): void {
  const client = useRpc();
  const setFreshness = useAppStore((state) => state.setAnalysisFreshness);

  const inFlight = useRef<string | undefined>(undefined);
  const lastAt = useRef(0);

  const check = useCallback(
    (force: boolean) => {
      if (!client || !repositoryPath || !analysisId || running) return;
      if (inFlight.current === analysisId) return;
      if (!force && Date.now() - lastAt.current < FRESHNESS_THROTTLE_MS) return;

      inFlight.current = analysisId;
      lastAt.current = Date.now();

      checkAnalysisFreshness(client, { repositoryPath, analysisId })
        .then(setFreshness)
        .catch((caught: unknown) => {
          console.warn('[freshness] The working tree could not be compared with the analysis.', caught);
        })
        .finally(() => {
          if (inFlight.current === analysisId) inFlight.current = undefined;
        });
    },
    [client, repositoryPath, analysisId, running, setFreshness],
  );

  // Every analysis that reaches the screen is checked once, whatever brought it there.
  useEffect(() => {
    check(true);
  }, [check]);

  useEffect(() => {
    const onFocus = () => check(false);
    const onVisible = () => {
      if (document.visibilityState === 'visible') check(false);
    };

    window.addEventListener('focus', onFocus);
    document.addEventListener('visibilitychange', onVisible);

    return () => {
      window.removeEventListener('focus', onFocus);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [check]);
}
