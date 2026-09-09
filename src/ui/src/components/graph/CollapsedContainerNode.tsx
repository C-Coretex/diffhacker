import { AlertTriangle, ChevronRight } from 'lucide-react';
import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';
import { translate as t } from '@/i18n/translate';
import { cn } from '@/lib/utils';
import { COLLAPSED_HEIGHT, COLLAPSED_WIDTH } from '@/graph/elkOptions';
import type { CollapsedContainerNodeData } from '@/graph/flowGraph';
import { projectColourStyle } from '@/graph/palette';
import { useAppStore } from '@/store/appStore';

/**
 * A cluster the reviewer folded away.
 *
 * It shows what a reader needs to decide whether to open it again — how many files, where the
 * reading starts, how big the change is, whether anything in it is risky, and which projects it
 * touches — and nothing else. A collapsed container that showed only a title would make collapsing
 * something you undo immediately.
 *
 * Edges with one end inside it are redrawn to point at this box; several that collapse onto the
 * same pair merge into one bundle edge. Edges wholly inside it disappear, because both of their
 * ends did.
 */
export function CollapsedContainerNode({ data }: NodeProps<Node<CollapsedContainerNodeData>>) {
  const { container, nodeCount, entryLabel, linesAdded, linesRemoved, riskCount, colourSlots, isMatch } =
    data;
  const toggle = useAppStore((state) => state.toggleContainerCollapsed);

  return (
    <div
      className={cn(
        'flex flex-col rounded-lg border-2 border-dashed border-border bg-muted/60 text-card-foreground',
        isMatch && 'ring-2 ring-ring',
      )}
      style={{ width: COLLAPSED_WIDTH, height: COLLAPSED_HEIGHT }}
      data-testid={`graph-container-${container.id}`}
      data-collapsed="true"
    >
      <Handle type="target" position={Position.Top} className="!h-1 !w-1 !border-0 !bg-transparent" />

      <button
        type="button"
        // @see ContainerNode: the chevron expands the cluster, it does not pin its card.
        onClick={(event) => {
          event.stopPropagation();
          toggle(container.id);
        }}
        className="flex items-center gap-2 px-3 py-2 text-left hover:bg-accent/50"
        aria-expanded={false}
        aria-label={t('analysis.graph.expandContainer', { title: container.title })}
      >
        <ChevronRight className="size-4 shrink-0 text-muted-foreground" aria-hidden />
        <span className="truncate text-sm font-semibold" title={container.title}>
          {container.title}
        </span>
        <span className="ml-auto shrink-0 rounded bg-background px-1.5 py-0.5 text-[10px] tabular-nums text-muted-foreground">
          {t('analysis.graph.fileCount', { count: nodeCount })}
        </span>
      </button>

      {entryLabel && (
        <div className="truncate px-3 text-[11px] text-muted-foreground" title={entryLabel}>
          {t('analysis.graph.startsAt', { file: entryLabel })}
        </div>
      )}

      <div className="mt-auto flex items-center gap-2 px-3 pb-2 text-[10px] text-muted-foreground">
        <span className="tabular-nums">
          {linesAdded === null && linesRemoved === null
            ? t('analysis.graph.noCounts')
            : `+${linesAdded ?? 0} −${linesRemoved ?? 0}`}
        </span>

        {riskCount > 0 && (
          <span className="flex items-center gap-0.5 text-destructive">
            <AlertTriangle className="size-3" aria-hidden />
            <span className="tabular-nums">{riskCount}</span>
          </span>
        )}

        <span className="ml-auto flex items-center gap-0.5" aria-hidden>
          {colourSlots.map((slot, index) => (
            <span
              key={`${slot ?? 'other'}-${index}`}
              className="size-2 rounded-full"
              style={{ background: projectColourStyle(slot).rail }}
            />
          ))}
        </span>
      </div>

      <Handle type="source" position={Position.Bottom} className="!h-1 !w-1 !border-0 !bg-transparent" />
    </div>
  );
}
