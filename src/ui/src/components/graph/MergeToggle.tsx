import { Combine } from 'lucide-react';
import type { AnalysisView } from '@/contracts';
import type { ResourceKey } from '@/i18n/translate';
import { useT } from '@/i18n/useT';
import { cn } from '@/lib/utils';
import { useAppStore } from '@/store/appStore';

/**
 * Whether an abstraction and its implementations are drawn as one box.
 *
 * One pressed-or-not button, styled like the grouping picker's, because it is the same kind of
 * control: a way of looking at the stored answer that costs nothing and never reaches the runner.
 *
 * Offered disabled with its reason, never hidden, when there is nothing to merge — and the reason says
 * which of two different things is true, because they need different responses from the reviewer.
 * An analysis nobody asked for groups is one a re-run would fix; a change with no abstraction beside
 * its implementations is simply that change.
 */
export function MergeToggle({ view }: { view: AnalysisView }) {
  const t = useT();
  const merge = useAppStore((state) => state.graphMergeImplementations);
  const setMerge = useAppStore((state) => state.setGraphMergeImplementations);

  const reason = unavailableReason(view);
  const pressed = merge && reason === null;

  return (
    <button
      type="button"
      onClick={() => setMerge(!merge)}
      disabled={reason !== null}
      aria-pressed={pressed}
      title={
        reason === null
          ? `${t('analysis.graph.merged.toggle')} — ${t('analysis.graph.merged.toggleBody')}`
          : t(reason)
      }
      data-testid="merge-implementations"
      className={cn(
        'flex items-center gap-1.5 rounded-md border border-border px-2 py-1 text-xs text-muted-foreground',
        'hover:bg-accent hover:text-foreground disabled:opacity-50 disabled:hover:bg-transparent',
        pressed && 'bg-accent text-foreground',
      )}
    >
      <Combine className="size-3.5" aria-hidden />
      {t('analysis.graph.merged.toggle')}
    </button>
  );
}

/** Why there is nothing to merge in this view, or null when there is something. */
export function unavailableReason(view: AnalysisView): ResourceKey | null {
  if (!view.implementationGroupsProduced) return 'analysis.graph.merged.notAsked';
  if (view.implementationGroups.length === 0) return 'analysis.graph.merged.none';
  return null;
}
