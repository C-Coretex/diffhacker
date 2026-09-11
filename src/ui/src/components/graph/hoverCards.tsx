import { useEffect, useState } from 'react';
import { CheckIcon, CopyIcon, FileDiffIcon } from 'lucide-react';
import type {
  AnalysisContainerInfo,
  AnalysisEdgeInfo,
  AnalysisNodeInfo,
  AnalysisNodeInfoState,
  AnalysisView,
  ChangedFileFactsInfo,
} from '@/contracts';
import { useT } from '@/i18n/useT';
import { copyText } from '@/lib/clipboard';
import { useAppStore } from '@/store/appStore';
import { Badge } from '@/components/ui/badge';
import { Markdown } from '@/components/analysis/Markdown';
import { ChangeStats, NodeExplanation } from '@/components/analysis/NodeExplanation';
import { RiskColumn } from '@/components/analysis/RiskList';
import { basename } from '@/graph/truncate';

/**
 * What a hover card says about a node, an edge and a cluster.
 *
 * Three components in one file, the way `edges.tsx` holds the three kinds of line: they are the same
 * card with different content, and the thing worth being able to check by reading down a page is
 * that all three keep risks in a column of their own.
 *
 * **Nothing here fetches anything.** Every field is read straight off the `AnalysisView` the screen
 * already has — which is the iteration's first fixed decision, and the reason it is worth stating
 * in a comment: the day someone adds "explain this node in more detail" behind a hover is the day
 * this product starts charging people for moving a mouse.
 */

const stateLabels = {
  changed: 'analysis.state.changed',
  added: 'analysis.state.added',
  deleted: 'analysis.state.deleted',
  unchanged_relevant: 'analysis.state.unchanged_relevant',
  risky: 'analysis.state.risky',
  entry_point: 'analysis.state.entry_point',
} as const satisfies Record<AnalysisNodeInfoState, string>;

/** Requirement 1: what changed, why, what it affects, what is worth knowing, and the numbers. */
export function NodeHoverCard({
  node,
  container,
  facts,
}: {
  node: AnalysisNodeInfo;
  container: AnalysisContainerInfo | undefined;
  facts: ChangedFileFactsInfo | undefined;
}) {
  const t = useT();

  return (
    <article className="flex flex-col">
      <header className="flex flex-col gap-1.5 border-b border-border px-3 py-2.5">
        <div className="flex items-start gap-2">
          <h3 className="min-w-0 flex-1 text-sm font-semibold leading-snug">{node.title}</h3>
          <CopyPathButton path={node.filePath} />
        </div>

        {/*
          Iteration 10 requirement 1's gesture, decided deliberately rather than by taking the click
          back. Iteration 9 spent the single click on keeping this card open — because hovering alone
          lost the card while the reviewer was reaching for it — so the diff opens from the card,
          which is where the explanation and the risks already are, or from a double-click on the box
          for anyone who would rather not aim at a button.
        */}
        <OpenDiffButton node={node} />

        <p className="break-all font-mono text-[11px] text-muted-foreground">
          {node.filePath}
          {node.symbol && ` · ${t('analysis.hover.nodeSymbol', { symbol: node.symbol })}`}
          {node.startLine > 0 &&
            ` · ${t('analysis.nodeLines', { start: node.startLine, end: node.endLine })}`}
        </p>

        <div className="flex flex-wrap items-center gap-1">
          {node.states.map((state) => (
            <Badge
              key={state}
              variant={state === 'risky' ? 'destructive' : 'outline'}
              className="px-1.5 py-0 text-[10px]"
            >
              {t(stateLabels[state])}
            </Badge>
          ))}
        </div>

        {/* Requirement 1's "change statistics": what git measured, not what the model claimed. */}
        <p className="text-[11px] text-muted-foreground">
          <ChangeStats facts={facts} />
          {' · '}
          {t('analysis.hover.nodePlace', {
            rank: node.rank,
            container: container?.title ?? '',
            importance: node.importance,
          })}
        </p>
      </header>

      <NodeExplanation node={node} className="p-3" />
    </article>
  );
}

/** Opens the diff panel on this node, and makes it the reviewer's current position. */
function OpenDiffButton({ node }: { node: AnalysisNodeInfo }) {
  const t = useT();
  const openDiff = useAppStore((state) => state.openDiffFor);

  return (
    <button
      type="button"
      onClick={() => openDiff(node.id, node.containerId)}
      title={t('analysis.graph.openDiffHint')}
      data-testid="open-diff"
      className="flex items-center gap-1.5 self-start rounded border border-border bg-secondary/60 px-2 py-1 text-xs font-medium hover:bg-accent"
    >
      <FileDiffIcon className="size-3.5" aria-hidden />
      {t('analysis.graph.openDiff')}
    </button>
  );
}

/** Requirement 2: what the relationship is, whether code backs it, and how the change affects it. */
export function EdgeHoverCard({
  edges,
  view,
  count,
}: {
  edges: readonly AnalysisEdgeInfo[];
  view: AnalysisView;
  count: number;
}) {
  const t = useT();
  const label = (id: string): string => basename(view.nodes.find((n) => n.id === id)?.filePath ?? id);

  return (
    <article className="flex flex-col">
      <header className="flex flex-col gap-1 border-b border-border px-3 py-2.5">
        <h3 className="text-sm font-semibold">{t('analysis.hover.edgeHeading')}</h3>
        {count > 1 && (
          // A bundle stands for several of the model's edges. Saying so, and then listing each one,
          // is the alternative to picking one of them and presenting it as the answer.
          <p className="text-[11px] text-muted-foreground">
            {t('analysis.graph.legendBundle')} · ×{count}
          </p>
        )}
      </header>

      <div className="flex flex-col divide-y divide-border">
        {edges.map((edge) => (
          <div
            key={`${edge.sourceNodeId}->${edge.targetNodeId}`}
            className="grid grid-cols-[1.6fr_1fr] gap-3 p-3"
          >
            <div className="flex min-w-0 flex-col gap-2">
              <p className="text-xs font-medium">
                {t('analysis.hover.edgeDirection', {
                  source: label(edge.sourceNodeId),
                  target: label(edge.targetNodeId),
                })}
              </p>

              <div className="flex flex-wrap items-center gap-1">
                <Badge
                  variant={edge.kind === 'direct' ? 'secondary' : 'outline'}
                  className="px-1.5 py-0 text-[10px]"
                >
                  {t(edge.kind === 'direct' ? 'analysis.edgeDirect' : 'analysis.edgeConceptual')}
                </Badge>
                {edge.crossesContainers && (
                  <Badge variant="outline" className="px-1.5 py-0 text-[10px]">
                    {t('analysis.edgeCrosses')}
                  </Badge>
                )}
              </div>

              {edge.explanation ? (
                <Markdown text={edge.explanation} className="text-sm leading-relaxed" />
              ) : (
                <p className="text-sm leading-relaxed">{t('analysis.hover.nothingWritten')}</p>
              )}

              <p className="break-all font-mono text-[10px] text-muted-foreground">
                {edge.sourceNodeId} → {edge.targetNodeId}
              </p>
            </div>

            <RiskColumn risks={edge.risks} className="min-w-0 self-start" />
          </div>
        ))}
      </div>
    </article>
  );
}

/** Requirement 3: the cluster-level explanation, with its own risks beside it. */
export function ContainerHoverCard({
  container,
  view,
}: {
  container: AnalysisContainerInfo;
  view: AnalysisView;
}) {
  const t = useT();
  const entry = view.nodes.find((node) => node.id === container.entryNodeId);

  return (
    <article className="flex flex-col">
      <header className="flex flex-col gap-1 border-b border-border px-3 py-2.5">
        <h3 className="text-sm font-semibold leading-snug">{container.title}</h3>
        <p className="text-[11px] text-muted-foreground">
          {t('analysis.hover.containerSize', { count: container.nodeIds.length })}
          {entry && ` · ${t('analysis.hover.containerEntry', { file: basename(entry.filePath) })}`}
        </p>
      </header>

      <div className="grid grid-cols-[1.6fr_1fr] gap-3 p-3">
        <div className="flex min-w-0 flex-col gap-2.5">
          <Markdown text={container.summary} className="text-sm leading-relaxed" />
          {container.explanation && (
            <Markdown
              text={container.explanation}
              className="text-sm leading-relaxed text-muted-foreground"
            />
          )}
        </div>

        <RiskColumn risks={container.risks} className="min-w-0 self-start" />
      </div>
    </article>
  );
}

/**
 * Requirement 7: every node offers a copy-path action.
 *
 * On the card rather than on the box, because a click on the box belongs to Iteration 10's diff
 * viewer and this iteration must not spend that gesture. The confirmation is the point of the
 * state: a copy that silently failed — which is a real possibility under a custom scheme, see
 * `lib/clipboard.ts` — has to say so rather than look like it worked.
 */
function CopyPathButton({ path }: { path: string }) {
  const t = useT();
  const [result, setResult] = useState<'idle' | 'copied' | 'failed'>('idle');

  useEffect(() => {
    if (result === 'idle') return;

    const timer = setTimeout(() => setResult('idle'), 2000);
    return () => clearTimeout(timer);
  }, [result]);

  return (
    <button
      type="button"
      onClick={() => void copyText(path).then((ok) => setResult(ok ? 'copied' : 'failed'))}
      aria-label={t('analysis.hover.copyPath')}
      className="flex shrink-0 items-center gap-1 rounded px-1.5 py-0.5 text-[11px] text-muted-foreground hover:bg-accent hover:text-foreground"
    >
      {result === 'copied' ? (
        <CheckIcon className="size-3.5" aria-hidden />
      ) : (
        <CopyIcon className="size-3.5" aria-hidden />
      )}
      {result === 'idle'
        ? t('analysis.hover.copyPath')
        : t(result === 'copied' ? 'analysis.hover.copied' : 'analysis.hover.copyFailed')}
    </button>
  );
}
