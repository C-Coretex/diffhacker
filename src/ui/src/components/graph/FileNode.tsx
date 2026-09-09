import { Handle, Position, type NodeProps, type Node } from '@xyflow/react';
import { AlertTriangle, ChevronsUp } from 'lucide-react';
import type { AnalysisNodeInfoState } from '@/contracts';
import { translate as t } from '@/i18n/translate';
import { cn } from '@/lib/utils';
import { NODE_HEIGHT, NODE_WIDTH } from '@/graph/elkOptions';
import type { FileNodeData } from '@/graph/flowGraph';
import { projectColourStyle } from '@/graph/palette';
import { basename, clamp, directoryContext } from '@/graph/truncate';

/**
 * One box on the diagram.
 *
 * Five pieces of information in 260 × 96 pixels (requirement 2), and the size is fixed because ELK
 * needs it before it can place anything and because three hundred identical rectangles are
 * scannable in a way three hundred different ones are not.
 *
 * ```
 * ┌──────────────────────────────────────────┐
 * │▏ ▲  Cache.cs                        ⚑ ⑤ │  rail · entry · basename · risk · rank
 * │▏ src/…/Storage/                          │  elided directory context
 * │▏ Evicts on tenant change                 │  the model's title, one line
 * │▏ +42 −7 · modified · DiffHacker.Core     │  stats · status · project
 * └──────────────────────────────────────────┘
 * ```
 *
 * **Rank is printed.** ELK places by edges, and a node the model ranked third with nothing pointing
 * at it is chained to its predecessor by a synthetic edge — a faithful ordering but not a claim
 * about dependency. Printing the rank means a reader who sees 3 beside 2 knows the ordering is the
 * model's, not the layout's. Hiding the number would have made the layout look more certain than it
 * is.
 *
 * State is border style plus a corner badge, never colour, because fill already encodes project
 * (§0.6) and because states co-occur — a node can be added and risky and the entry point at once.
 */
export function FileNode({ data }: NodeProps<Node<FileNodeData>>) {
  const { node, facts, colourSlot, isEntry, isMatch, isFocused } = data;
  const colour = projectColourStyle(colourSlot);
  const states = new Set(node.states);

  return (
    <div
      className={cn(
        'relative flex flex-col overflow-hidden rounded-md border-2 text-card-foreground shadow-sm',
        borderFor(states),
        isMatch && 'ring-2 ring-ring ring-offset-1',
        isFocused && 'ring-4 ring-primary ring-offset-2',
      )}
      style={{
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        background: colour.fill,
        borderColor: states.has('risky') ? undefined : colour.rail,
      }}
      data-testid={`graph-node-${node.id}`}
      data-node-id={node.id}
      data-entry={isEntry ? 'true' : undefined}
    >
      {/* The saturated edge of the project colour. Colour never carries the category alone —
          the project name is printed in the footer and repeated in the legend — but this is what
          makes two boxes from the same project group at a glance. */}
      <span aria-hidden className="absolute inset-y-0 left-0 w-1" style={{ background: colour.rail }} />

      <Handle type="target" position={Position.Top} className="!h-1 !w-1 !border-0 !bg-transparent" />

      <div className="flex items-start gap-1 py-1 pl-3 pr-2">
        {isEntry && (
          <ChevronsUp
            className="mt-0.5 size-3.5 shrink-0 text-primary"
            aria-label={t('analysis.graph.entryPoint')}
          />
        )}
        <span className="min-w-0 flex-1 truncate text-xs font-semibold" title={node.filePath}>
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

      <div className="truncate pl-3 pr-2 text-[10px] text-muted-foreground" title={node.filePath}>
        {directoryContext(node.filePath)}
      </div>

      <div className="mt-0.5 line-clamp-2 flex-1 pl-3 pr-2 text-[11px] leading-tight" title={node.title}>
        {clamp(node.title, 60)}
      </div>

      <div className="flex items-center gap-1 truncate pb-1 pl-3 pr-2 text-[10px] text-muted-foreground">
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

      <Handle type="source" position={Position.Bottom} className="!h-1 !w-1 !border-0 !bg-transparent" />
    </div>
  );
}

/**
 * Line counts, or an honest silence.
 *
 * Absent rather than zero all the way from git: a binary or a submodule pointer has no countable
 * line change, and "+0 −0" would claim it was touched and nothing happened.
 */
function Stats({
  added,
  removed,
  binary,
}: {
  added?: number;
  removed?: number;
  binary?: boolean;
}) {
  if (binary) return <span>{t('analysis.graph.binary')}</span>;
  if (added === undefined && removed === undefined) return <span>{t('analysis.graph.noCounts')}</span>;

  return (
    <span className="tabular-nums">
      {added !== undefined && <span className="text-emerald-700 dark:text-emerald-400">+{added}</span>}
      {added !== undefined && removed !== undefined && ' '}
      {removed !== undefined && <span className="text-rose-700 dark:text-rose-400">−{removed}</span>}
    </span>
  );
}

/**
 * The border says what happened to the file. Distinguishable at low zoom, where a badge is a
 * smudge: solid for changed, dashed for added, dotted for deleted, double for a file that did not
 * change but matters anyway, and a destructive colour for risky over whichever of those applies.
 */
function borderFor(states: ReadonlySet<AnalysisNodeInfoState>): string {
  const shape = states.has('added')
    ? 'border-dashed'
    : states.has('deleted')
      ? 'border-dotted'
      : states.has('unchanged_relevant')
        ? 'border-double border-4'
        : 'border-solid';

  return states.has('risky') ? `${shape} !border-destructive` : shape;
}

/**
 * The git status word, falling back to the model's state when the changeset facts are missing —
 * which is what an analysis stored before schema 1.8 looks like.
 */
function statusLabel(
  status: string | undefined,
  states: ReadonlySet<AnalysisNodeInfoState>,
): string {
  if (status) return status;
  if (states.has('added')) return t('analysis.state.added');
  if (states.has('deleted')) return t('analysis.state.deleted');
  if (states.has('unchanged_relevant')) return t('analysis.state.unchanged_relevant');
  return t('analysis.state.changed');
}
