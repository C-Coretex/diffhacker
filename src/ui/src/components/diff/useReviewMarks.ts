import { useCallback } from 'react';
import { describeError } from '@/i18n/errors';
import { setNodesReviewed } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';

export interface ReviewMarks {
  /** Whether one node is marked. */
  readonly isReviewed: (nodeId: string) => boolean;

  /**
   * Marks or unmarks nodes. Optimistic locally, reconciled against the host's answer.
   *
   * Takes a list although every caller today passes one id, because the call underneath does: one
   * round trip for a whole container is the shape `analysis.setReviewed` was given, and a
   * single-id signature here would have to be widened again to use it.
   */
  readonly mark: (nodeIds: readonly string[], reviewed: boolean) => void;
}

/**
 * Marking nodes reviewed — requirement 7.
 *
 * Applied to the store first and sent afterwards. A checkbox that waits for a round trip before it
 * moves feels broken at three hundred of them, and the failure it is protecting against is one the
 * host reports honestly: a rejected mark puts the set back the way the host says it is and shows why.
 *
 * Persistence is the host's, not this hook's: the marks live on the stored analysis (schema 6's
 * `reviewed_json`), which is what makes them survive a restart, and the response carries the whole
 * set so the interface never has to reconcile a delta.
 *
 * Used from both surfaces that offer the mark — the button on the box and the button in the panel
 * header — which is why it takes a repository path rather than a whole `AnalysisView`: a node box has
 * the node, not the analysis, and handing it one to reach a single string would be handing it one to
 * re-render on.
 */
export function useReviewMarks(repositoryPath: string | undefined): ReviewMarks {
  const client = useRpc();
  const reviewed = useAppStore((state) => state.reviewedNodeIds);
  const setReviewed = useAppStore((state) => state.setReviewed);
  const applyReviewedState = useAppStore((state) => state.applyReviewedState);
  const failReviewed = useAppStore((state) => state.failReviewed);

  const mark = useCallback(
    (nodeIds: readonly string[], next: boolean) => {
      if (nodeIds.length === 0) return;

      const before = useAppStore.getState().reviewedNodeIds;
      setReviewed(nodeIds, next);

      if (!client || !repositoryPath) return;

      setNodesReviewed(client, { repositoryPath, nodeIds: [...nodeIds], reviewed: next })
        .then((state) => applyReviewedState(state.reviewedNodeIds))
        .catch((caught: unknown) => {
          // Back to exactly what it was, not to "unmarked": the reviewer may have been unmarking.
          applyReviewedState([...before]);
          failReviewed(describeError(caught));
        });
    },
    [client, repositoryPath, setReviewed, applyReviewedState, failReviewed],
  );

  return {
    isReviewed: useCallback((nodeId: string) => reviewed.has(nodeId), [reviewed]),
    mark,
  };
}

/** How much of the change, and of one cluster, has been read. Requirement 7's indicators. */
export function reviewProgress(
  ids: readonly string[],
  reviewed: ReadonlySet<string>,
): { reviewedCount: number; total: number } {
  return {
    reviewedCount: ids.reduce((count, id) => (reviewed.has(id) ? count + 1 : count), 0),
    total: ids.length,
  };
}
