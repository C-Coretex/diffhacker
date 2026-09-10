import { Handle, Position, type NodeProps, type Node } from '@xyflow/react';
import { AlertTriangle, CheckCheck, ChevronsUp } from 'lucide-react';
import type { AnalysisNodeInfoState } from '@/contracts';
import { translate as t } from '@/i18n/translate';
import { cn } from '@/lib/utils';
import { NODE_HEIGHT, NODE_WIDTH } from '@/graph/elkOptions';
import type { FileNodeData } from '@/graph/flowGraph';
import { projectColourStyle } from '@/graph/palette';
import { basename, clamp, directoryContext } from '@/graph/truncate';
import { NodeActions } from './NodeActions';

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
 *
 * **Importance is the sixth channel**, added in Iteration 9: a box the model ranked 1 or 2 fades and
 * its text mutes; one ranked 4 or 5 gets a heavier name and a wider rail. Nothing changes size, so
 * ELK never sees it, and the fade has a floor — §0.2.5 says every changed file is on the diagram,
 * and a box nobody can read is not really on it. It goes back to full the moment the pointer is on
 * it, the search matches it, or a jump lands on it.
 *
 * **Iteration 10 adds two more, and neither reuses a channel that was already spent.** Every colour,
 * border style and badge on this box already means something, so:
 *
 * - *Current* — the node the diff panel is showing — is a heavy inset outline drawn **inside** the
 *   box, distinct from the search ring and the jump ring, which are both drawn outside it. That is
 *   requirement 6: the reviewer's position is visible in the diagram at all times, including when the
 *   panel is at its widest and the diagram is down to a rail.
 * - *Reviewed* is a tick in the footer and a lighter box. It reads as "done with", which is what it
 *   means, and it never fades below the importance floor for the same reason that floor exists.
 *
 * **And the box carries its own controls** — open the diff, hand the file to an external editor, mark
 * it read — drawn over the footer when the pointer is on it. They are `NodeActions`, and the note
 * there explains why they arrive with the pointer rather than sitting on three hundred boxes at once.
 */
export function FileNode({ data }: NodeProps<Node<FileNodeData>>) {
  const { node, facts, colourSlot, isEntry, isMatch, isFocused, isCurrent, isReviewed } = data;
  const colour = projectColourStyle(colourSlot);
  const states = new Set(node.states);
  const emphasis = emphasisFor(node.importance, isMatch || isFocused || isCurrent);

  return (
    <div
      className={cn(
        'group relative flex flex-col overflow-hidden rounded-md border-2 text-card-foreground shadow-sm',
        borderFor(states),
        emphasis.box,
        isMatch && 'ring-2 ring-ring ring-offset-1',
        isFocused && 'ring-4 ring-primary ring-offset-2',
        // Inset rather than a third ring: two rings outside the box already mean two other things,
        // and at the zoom a three-hundred-node diagram sits at they would be hard to tell apart.
        isCurrent && 'outline-[3px] outline-offset-[-3px] outline-primary',
        isReviewed && !isCurrent && 'opacity-80 saturate-50',
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
      data-emphasis={emphasis.level}
      data-current={isCurrent ? 'true' : undefined}
      data-reviewed={isReviewed ? 'true' : undefined}
    >
      {/* The saturated edge of the project colour. Colour never carries the category alone —
          the project name is printed in the footer and repeated in the legend — but this is what
          makes two boxes from the same project group at a glance. */}
      <span
        aria-hidden
        className={cn('absolute inset-y-0 left-0', emphasis.rail)}
        style={{ background: colour.rail }}
      />

      <Handle type="target" position={Position.Top} className="!h-1 !w-1 !border-0 !bg-transparent" />

      <div className="flex items-start gap-1 py-1 pl-3 pr-2">
        {isEntry && (
          <ChevronsUp
            className="mt-0.5 size-3.5 shrink-0 text-primary"
            aria-label={t('analysis.graph.entryPoint')}
          />
        )}
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

      <div className="truncate pl-3 pr-2 text-[10px] text-muted-foreground" title={node.filePath}>
        {directoryContext(node.filePath)}
      </div>

      <div
        className={cn('mt-0.5 line-clamp-2 flex-1 pl-3 pr-2 text-[11px] leading-tight', emphasis.title)}
        title={node.title}
      >
        {clamp(node.title, 60)}
      </div>

      <div className="flex items-center gap-1 truncate pb-1 pl-3 pr-2 text-[10px] text-muted-foreground">
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

      {/*
        The controls that belong to this file, drawn over the footer when the pointer is on the box
        or while the panel is showing it. See `NodeActions` for why they are not always there.
      */}
      <NodeActions node={node} facts={facts} isReviewed={isReviewed} isCurrent={isCurrent} />

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
 * How loudly a box speaks, from the model's importance rank (requirement 6).
 *
 * The floor is the point. §0.2.5 says every changed file appears in the graph, so "de-emphasised"
 * can only mean *quieter*, never smaller, never hidden, never harder to find — the box keeps its
 * size, its border style, its badges and its project colour, all of which §0.6 already spent on
 * something else. What is left is opacity and weight, and 70 % is where the fade stops: enough that
 * a screen of thirty boxes visibly sorts itself, little enough that a faded box is still readable
 * without doing anything.
 *
 * `lifted` — a search match or a jump — and `group-hover` both restore full strength, so looking at
 * a trivial node is never a worse experience than looking at an important one.
 */
function emphasisFor(
  importance: number,
  lifted: boolean,
): { level: string; box: string; rail: string; fileName: string; title: string } {
  if (!lifted && importance <= 2) {
    return {
      level: 'low',
      box: 'opacity-70 transition-opacity hover:opacity-100',
      rail: 'w-1',
      fileName: 'font-medium',
      title: 'text-muted-foreground',
    };
  }

  if (importance >= 4) {
    return { level: 'high', box: '', rail: 'w-1.5', fileName: 'font-bold', title: 'font-medium' };
  }

  return { level: 'normal', box: '', rail: 'w-1', fileName: 'font-semibold', title: '' };
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
