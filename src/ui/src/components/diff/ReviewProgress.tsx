import { CircleCheckBigIcon } from 'lucide-react';
import { useT } from '@/i18n/useT';
import { useAppStore } from '@/store/appStore';
import { cn } from '@/lib/utils';
import { reviewProgress } from './useReviewMarks';

/**
 * How much of something has been read — requirement 7's indicator.
 *
 * "This is what makes a 300-node review survivable", says the iteration, and the reason is that a
 * reviewer with three hundred boxes in front of them cannot hold in their head which ones they have
 * already opened. A bar and a count answer that without them having to.
 *
 * Drawn twice: once across the whole change on the band's strip, and once per cluster in the overview
 * list. Both read the same set, so they can never disagree about a node.
 */
export function ReviewProgress({
  nodeIds,
  className,
  showLabel = false,
}: {
  nodeIds: readonly string[];
  className?: string;

  /** The strip says what the bar means; a row in a list of clusters does not have room to. */
  showLabel?: boolean;
}) {
  const t = useT();
  const reviewed = useAppStore((state) => state.reviewedNodeIds);

  const { reviewedCount, total } = reviewProgress(nodeIds, reviewed);
  if (total === 0) return null;

  const fraction = reviewedCount / total;
  const complete = reviewedCount === total;

  return (
    <span
      className={cn('flex min-w-0 items-center gap-1.5 text-xs text-muted-foreground', className)}
      data-testid="review-progress"
      data-reviewed={reviewedCount}
      data-total={total}
    >
      {showLabel && <CircleCheckBigIcon className="size-3.5 shrink-0" aria-hidden />}

      {/*
        A bar and a number, not a percentage. "212 of 312" is what a reviewer is actually tracking;
        "68%" is the same fact with the useful part removed.
      */}
      <span
        role="progressbar"
        aria-label={t('analysis.diff.reviewedProgressLabel')}
        aria-valuenow={reviewedCount}
        aria-valuemin={0}
        aria-valuemax={total}
        className="h-1.5 w-16 shrink-0 overflow-hidden rounded-full bg-border"
      >
        <span
          className={cn('block h-full rounded-full', complete ? 'bg-primary' : 'bg-primary/60')}
          style={{ width: `${Math.round(fraction * 100)}%` }}
        />
      </span>

      <span className="tabular-nums">
        {t('analysis.diff.reviewedProgress', { reviewed: reviewedCount, total })}
      </span>
    </span>
  );
}
