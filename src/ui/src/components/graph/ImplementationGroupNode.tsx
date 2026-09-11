import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';
import { AlertTriangle, CheckCheck, ChevronsUp, CornerDownRight, Diamond } from 'lucide-react';
import { translate as t } from '@/i18n/translate';
import { cn } from '@/lib/utils';
import { NODE_WIDTH, MERGED_HEADER, mergedHeight } from '@/graph/elkOptions';
import type { FileNodeData, ImplementationGroupNodeData } from '@/graph/flowGraph';
import { projectColourStyle } from '@/graph/palette';
import { basename, clamp } from '@/graph/truncate';
import { NodeActions } from './NodeActions';
import { borderFor, emphasisFor, Stats, statusLabel } from './nodeParts';

/**
 * An abstraction and its implementations, as one box.
 *
 * ```
 * ┌──────────────────────────────────────────┐
 * │ ◇ Abstraction · 2 implementing           │  what this box is
 * │┌────────────────────────────────────────┐│
 * ││▏ ◇ ICacheStore.cs                   ① ││  the abstraction: its own row, its own border
 * ││▏ Declares the eviction contract        ││
 * ││▏ +4 −1 · modified · DiffHacker.Core    ││
 * │└────────────────────────────────────────┘│
 * │┌────────────────────────────────────────┐│
 * ││▏ ↳ MemoryCacheStore.cs              ③ ││  each implementation beneath it
 * ││▏ …                                     ││
 * │└────────────────────────────────────────┘│
 * └──────────────────────────────────────────┘
 * ```
 *
 * **Every row is still its node.** It carries the node's own test id, state border, importance,
 * reviewed tick and controls, and a click or double-click on it means that node — the surface finds
 * the row from the event rather than treating the box as one thing. The merge changes how many boxes
 * the eye has to track; it does not change what any one of them says, and §0.2.5 still counts every
 * row as a node on the diagram.
 *
 * Edges attach to the box, not to a row: the relationship card still names both ends exactly.
 */
export function ImplementationGroupNode({ data }: NodeProps<Node<ImplementationGroupNodeData>>) {
  const { rows } = data;
  const abstraction = rows[0];
  if (!abstraction) return null;

  const colour = projectColourStyle(abstraction.colourSlot);

  return (
    <div
      className="flex flex-col gap-0.5 overflow-hidden rounded-lg border-2 bg-card p-0.5 text-card-foreground shadow-sm"
      style={{ width: NODE_WIDTH, height: mergedHeight(rows.length), borderColor: colour.rail }}
      data-testid={`graph-merged-${abstraction.node.id}`}
      data-merged="true"
    >
      <Handle type="target" position={Position.Top} className="!h-1 !w-1 !border-0 !bg-transparent" />

      <div
        className="flex shrink-0 items-center gap-1 px-1.5 text-[10px] font-medium text-muted-foreground"
        style={{ height: MERGED_HEADER - 6 }}
      >
        <Diamond className="size-3 shrink-0" aria-hidden />
        <span className="truncate">
          {t('analysis.graph.merged.heading', { count: rows.length - 1 })}
        </span>
      </div>

      {rows.map((row, index) => (
        <MemberRow key={row.node.id} row={row} isAbstraction={index === 0} />
      ))}

      <Handle type="source" position={Position.Bottom} className="!h-1 !w-1 !border-0 !bg-transparent" />
    </div>
  );
}

/** One node inside the box: `FileNode` drawn shorter, without the directory line. */
function MemberRow({ row, isAbstraction }: { row: FileNodeData; isAbstraction: boolean }) {
  const { node, facts, colourSlot, isEntry, isMatch, isFocused, isCurrent, isReviewed } = row;
  const colour = projectColourStyle(colourSlot);
  const states = new Set(node.states);
  const emphasis = emphasisFor(node.importance, isMatch || isFocused || isCurrent);
  const RoleIcon = isAbstraction ? Diamond : CornerDownRight;

  return (
    <div
      className={cn(
        'group relative flex min-h-0 flex-1 flex-col overflow-hidden rounded-md border-2',
        borderFor(states),
        emphasis.box,
        isMatch && 'ring-2 ring-ring',
        isFocused && 'ring-4 ring-primary',
        isCurrent && 'outline-[3px] outline-offset-[-3px] outline-primary',
        isReviewed && !isCurrent && 'opacity-80 saturate-50',
      )}
      style={{
        background: colour.fill,
        borderColor: states.has('risky') ? undefined : colour.rail,
      }}
      data-testid={`graph-node-${node.id}`}
      data-node-id={node.id}
      data-role={isAbstraction ? 'abstraction' : 'implementation'}
      data-entry={isEntry ? 'true' : undefined}
      data-emphasis={emphasis.level}
      data-current={isCurrent ? 'true' : undefined}
      data-reviewed={isReviewed ? 'true' : undefined}
    >
      <span
        aria-hidden
        className={cn('absolute inset-y-0 left-0', emphasis.rail)}
        style={{ background: colour.rail }}
      />

      <div className="flex items-start gap-1 pt-0.5 pl-3 pr-2">
        {isEntry && (
          <ChevronsUp
            className="mt-0.5 size-3.5 shrink-0 text-primary"
            aria-label={t('analysis.graph.entryPoint')}
          />
        )}
        <RoleIcon
          className="mt-0.5 size-3 shrink-0 text-muted-foreground"
          aria-label={t(isAbstraction ? 'analysis.graph.merged.abstraction' : 'analysis.graph.merged.implementation')}
        />
        <span className={cn('min-w-0 flex-1 truncate text-xs', emphasis.fileName)} title={node.filePath}>
          {basename(node.filePath)}
        </span>
        {states.has('risky') && (
          <AlertTriangle
            className="size-3.5 shrink-0 text-destructive"
            aria-label={t('analysis.graph.risky')}
          />
        )}
        <span
          className="shrink-0 rounded bg-background/60 px-1 text-[10px] font-medium tabular-nums text-muted-foreground"
          title={t('analysis.graph.rankTitle')}
        >
          {node.rank}
        </span>
      </div>

      <div
        className={cn('line-clamp-1 flex-1 pl-3 pr-2 text-[11px] leading-tight', emphasis.title)}
        title={node.title}
      >
        {clamp(node.title, 60)}
      </div>

      <div className="flex items-center gap-1 truncate pb-0.5 pl-3 pr-2 text-[10px] text-muted-foreground">
        {isReviewed && (
          <CheckCheck className="size-3 shrink-0 text-primary" aria-label={t('analysis.graph.reviewed')} />
        )}
        <Stats added={facts?.linesAdded} removed={facts?.linesRemoved} binary={facts?.isBinary} />
        <span aria-hidden>·</span>
        <span>{statusLabel(facts?.status, states)}</span>
        {facts?.project && (
          <>
            <span aria-hidden>·</span>
            <span className="truncate" title={facts.project}>
              {facts.project}
            </span>
          </>
        )}
      </div>

      <NodeActions node={node} facts={facts} isReviewed={isReviewed} isCurrent={isCurrent} />
    </div>
  );
}
