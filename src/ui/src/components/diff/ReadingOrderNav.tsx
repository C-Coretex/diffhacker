import { ChevronLeftIcon, ChevronRightIcon } from 'lucide-react';
import type { AnalysisView } from '@/contracts';
import { useT } from '@/i18n/useT';
import { useAppStore } from '@/store/appStore';
import { containerQueue } from './containerQueue';
import { readingPosition } from './neighbours';

/**
 * Previous and next, as buttons rather than as chevrons, at the top of the panel.
 *
 * Requirement 5's linear path. It was a pair of icons tucked into the navigation section at the
 * bottom, and that was wrong for the thing a reviewer presses on every one of three hundred files:
 * it is the control this panel is driven by, so it is named, it is reachable without scrolling, and
 * it says where in the change the reviewer is between its two ends.
 *
 * **It walks the cluster when a cluster was opened whole.** "Next" then means the next file in the
 * queue the reviewer chose, not the next file in the change — following the analysis's own order
 * either way, over whichever list is on screen. Which of the two it is is never left to be inferred
 * from the number: the cluster strip directly above it names the cluster and counts its files, and it
 * is drawn if and only if this is walking one.
 *
 * Both ends stop rather than wrap. A reviewer at the last file who is returned to the first has been
 * told they are done in the least useful way available.
 */
export function ReadingOrderNav({ view, nodeId }: { view: AnalysisView; nodeId: string }) {
  const t = useT();
  const openDiff = useAppStore((state) => state.openDiffFor);
  const containerId = useAppStore((state) => state.diffContainerId);

  const queue = containerQueue(view, containerId);
  const path = queue.length > 0 ? queue.map((node) => node.id) : view.readingOrder;

  const reading = readingPosition({ ...view, readingOrder: path }, nodeId);
  if (reading.position === 0) return null;

  const go = (id: string | null) => {
    if (!id) return;
    const node = view.nodes.find((candidate) => candidate.id === id);
    if (node) openDiff(node.id, node.containerId);
  };

  return (
    <div className="flex items-center gap-1" data-testid="reading-order">
      <button
        type="button"
        disabled={!reading.previousId}
        onClick={() => go(reading.previousId)}
        title={t('analysis.diff.readingOrderPreviousHint')}
        data-testid="reading-order-previous"
        className="flex items-center gap-1 rounded border border-border px-2 py-1 text-[11px] hover:bg-accent disabled:opacity-40 disabled:hover:bg-transparent"
      >
        <ChevronLeftIcon className="size-3.5" aria-hidden />
        {t('analysis.diff.readingOrderPrevious')}
      </button>

      <span
        className="px-1 text-[11px] tabular-nums text-muted-foreground"
        data-testid="reading-order-position"
        data-scope={queue.length > 0 ? 'container' : 'analysis'}
      >
        {t('analysis.diff.readingOrderPosition', {
          position: reading.position,
          total: reading.total,
        })}
      </span>

      <button
        type="button"
        disabled={!reading.nextId}
        onClick={() => go(reading.nextId)}
        title={t('analysis.diff.readingOrderNextHint')}
        data-testid="reading-order-next"
        className="flex items-center gap-1 rounded border border-border px-2 py-1 text-[11px] hover:bg-accent disabled:opacity-40 disabled:hover:bg-transparent"
      >
        {t('analysis.diff.readingOrderNext')}
        <ChevronRightIcon className="size-3.5" aria-hidden />
      </button>
    </div>
  );
}
