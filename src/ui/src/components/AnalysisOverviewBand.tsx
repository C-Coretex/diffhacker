import { ChevronDown, ChevronRight, TriangleAlertIcon } from 'lucide-react';
import type { AnalysisNodeInfo, AnalysisNodeInfoState, AnalysisView } from '@/contracts';
import { formatCount } from '@/i18n/format';
import type { ResourceKey } from '@/i18n/translate';
import { useT } from '@/i18n/useT';
import { Badge } from '@/components/ui/badge';
import { Separator } from '@/components/ui/separator';
import { Markdown } from '@/components/analysis/Markdown';
import { RiskList } from '@/components/analysis/RiskList';
import { collectRisks, RiskRegister } from '@/components/analysis/RiskRegister';
import { ReviewProgress } from '@/components/diff/ReviewProgress';
import { useAppStore } from '@/store/appStore';

const stateLabels = {
  changed: 'analysis.state.changed',
  added: 'analysis.state.added',
  deleted: 'analysis.state.deleted',
  unchanged_relevant: 'analysis.state.unchanged_relevant',
  risky: 'analysis.state.risky',
  entry_point: 'analysis.state.entry_point',
} as const satisfies Record<AnalysisNodeInfoState, string>;

/**
 * Everything about the analysed change that is not the diagram, across the top of the screen.
 *
 * This was a left rail through Iteration 8 and is a band now, for two reasons that point the same
 * way. The diagram is the product and a three-hundred-box graph wants the width; and what a
 * reviewer needs *permanently* on screen is two things — what the change does, and what it risks —
 * which fit in a strip and do not need a column.
 *
 * ```
 * ┌──────────────────────────────────┬─────────────────┐
 * │ WHAT THIS CHANGE DOES            │ ⚠ RISKS  (14)   │  always
 * │ the model's summary              │ the change-wide │
 * │ ▸ Overview                       │ ones            │
 * ├──────────────────────────────────┴─────────────────┤
 * │ reading order │ clusters │ numbers │ this run      │  the toggle
 * │ every risk in one list · diagnostics · ▸ Details   │
 * └────────────────────────────────────────────────────┘
 * ```
 *
 * Risks are a column, never a paragraph inside the summary. §0.2 keeps the two apart from the
 * schema down; this is the top of the screen honouring it, and the hover cards honour it again on
 * every node, edge and cluster.
 *
 * The band spans the whole width deliberately: Iteration 10 adds a diff panel beside the graph, and
 * a panel that can expand to full width would have had to fight a rail for the same space. Above
 * both, it fights neither.
 */
export function AnalysisOverviewBand({ view }: { view: AnalysisView }) {
  const t = useT();
  const open = useAppStore((state) => state.graphOverviewOpen);
  const setOpen = useAppStore((state) => state.setGraphOverviewOpen);
  const showSummary = useAppStore((state) => state.graphBandOpen);
  const setShowSummary = useAppStore((state) => state.setGraphBandOpen);

  // Counted here rather than read from `statistics.riskCount` so the number beside the heading and
  // the list behind the toggle can never disagree — both come from the same walk of the document.
  const riskCount = collectRisks(view).length;

  return (
    <div className="shrink-0 border-b border-border bg-card/40">
      {/*
        The summary and the risks fold away too. A reviewer who has read them once is looking at the
        graph, and prose they have already read is the first thing that should give the canvas its
        height back. Folded, the strip still says how many risks there are — the count is the part
        you want to see without asking.
      */}
      <div className="flex items-center gap-3 px-6 pt-2">
        <button
          type="button"
          onClick={() => setShowSummary(!showSummary)}
          aria-expanded={showSummary}
          className="flex items-center gap-1.5 text-left text-xs font-semibold uppercase tracking-wide text-muted-foreground hover:text-foreground"
        >
          <Chevron open={showSummary} />
          {t('analysis.summaryHeading')}
        </button>

        {!showSummary && (
          <span className="flex min-w-0 items-center gap-1.5 text-xs text-muted-foreground">
            <TriangleAlertIcon className="size-3.5 shrink-0 text-warning-foreground" aria-hidden />
            {t('analysis.riskTotal', { count: riskCount })}
          </span>
        )}

        {/*
          Requirement 7's overall indicator, on the strip rather than inside the fold: how much of a
          three-hundred-file review is left is the one number a reviewer wants without asking, and it
          sits beside the risk count for the same reason.
        */}
        <ReviewProgress
          nodeIds={view.nodes.map((node) => node.id)}
          showLabel
          className="ml-4"
        />

        <button
          type="button"
          onClick={() => setOpen(!open)}
          aria-expanded={open}
          className="ml-auto flex items-center gap-1.5 text-left text-sm font-medium hover:text-primary"
        >
          <Chevron open={open} />
          {t('analysis.overview.toggle')}
        </button>
      </div>

      {showSummary && (
        <div className="grid grid-cols-[minmax(0,1.7fr)_minmax(0,1fr)] gap-6 px-6 pb-3 pt-2">
          <Markdown text={view.summary} className="text-sm leading-relaxed" />

          <section className="flex min-w-0 flex-col gap-2 rounded-md border border-warning/30 bg-warning/10 p-3">
            <h2 className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-warning-foreground">
              <TriangleAlertIcon className="size-3.5" aria-hidden />
              {/* Its own element rather than a bare text node, so the heading is addressable on its
                  own even though a count sits beside it. */}
              <span>{t('analysis.risksHeading')}</span>
              {riskCount > 0 && (
                <span className="font-normal normal-case tracking-normal text-muted-foreground">
                  {t('analysis.riskTotal', { count: riskCount })}
                </span>
              )}
            </h2>

            <div className="max-h-32 overflow-y-auto pr-1">
              <RiskList risks={view.overallRisks} />
            </div>
          </section>
        </div>
      )}

      {open && <Expanded view={view} />}
    </div>
  );
}

function Chevron({ open }: { open: boolean }) {
  return open ? (
    <ChevronDown className="size-4 shrink-0" aria-hidden />
  ) : (
    <ChevronRight className="size-4 shrink-0" aria-hidden />
  );
}

/** Requirement 5's overview, unfolded. Capped and scrolling, so the diagram keeps its height. */
function Expanded({ view }: { view: AnalysisView }) {
  const t = useT();

  return (
    <div className="max-h-[45vh] overflow-y-auto border-t border-border px-6 py-4">
      <p className="pb-4 text-xs text-muted-foreground">{t('analysis.overview.toggleBody')}</p>

      {/*
        Two rows of three, grouped by shape rather than by subject: three lists, then three tables.
        A grid row is as tall as its tallest column, so mixing a ten-row table into a row of short
        lists leaves most of the band empty and pushes what follows out of sight — and what follows
        is the risk register, which is the thing requirement 5 exists for.
      */}
      <div className="grid gap-6 lg:grid-cols-3">
        <ReadingOrder view={view} />
        <Clusters view={view} />
        <RiskRegister view={view} />
      </div>

      <Separator className="my-6" />

      <div className="grid gap-6 lg:grid-cols-3">
        <Statistics view={view} />
        <RunMetadata view={view} />
        <Diagnostics view={view} />
      </div>

      <Separator className="my-6" />

      <DetailsDisclosure view={view} />
    </div>
  );
}

/** The one path through the whole change, crossing cluster boundaries. */
function ReadingOrder({ view }: { view: AnalysisView }) {
  const t = useT();
  const reveal = useAppStore((state) => state.revealGraphNode);
  const nodesById = new Map(view.nodes.map((node) => [node.id, node]));

  return (
    <section className="flex min-w-0 flex-col gap-2">
      <div>
        <h3 className="text-sm font-semibold">{t('analysis.overview.readingOrderHeading')}</h3>
        <p className="text-xs text-muted-foreground">{t('analysis.overview.readingOrderBody')}</p>
      </div>

      {view.readingOrder.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('analysis.overview.readingOrderEmpty')}</p>
      ) : (
        <ol className="flex max-h-64 flex-col overflow-y-auto pr-1" data-testid="reading-order">
          {view.readingOrder.map((nodeId, index) => {
            const node = nodesById.get(nodeId);

            return (
              <li key={`${nodeId}-${index}`}>
                <button
                  type="button"
                  disabled={node === undefined}
                  onClick={() => node && reveal(node.id, node.containerId)}
                  className="flex w-full items-baseline gap-2 rounded px-1 py-0.5 text-left hover:bg-accent disabled:pointer-events-none"
                >
                  <span className="w-6 shrink-0 text-right text-[11px] tabular-nums text-muted-foreground">
                    {index + 1}
                  </span>
                  <span className="min-w-0 flex-1 truncate text-xs" title={nodeId}>
                    {node?.title ?? nodeId}
                  </span>
                </button>
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}

/** The clusters, in the order the model wants them read, with their sizes. */
function Clusters({ view }: { view: AnalysisView }) {
  const t = useT();
  const reveal = useAppStore((state) => state.revealGraphNode);
  const nodesById = new Map(view.nodes.map((node) => [node.id, node]));

  return (
    <section className="flex min-w-0 flex-col gap-2">
      <h3 className="text-sm font-semibold">{t('analysis.overview.clustersHeading')}</h3>

      <ol className="flex max-h-64 flex-col gap-1 overflow-y-auto pr-1" data-testid="cluster-list">
        {view.containers.map((container) => {
          const risks =
            container.risks.length +
            container.nodeIds.reduce(
              (total, id) => total + (nodesById.get(id)?.risks.length ?? 0),
              0,
            );

          return (
            <li key={container.id}>
              <button
                type="button"
                onClick={() => reveal(container.entryNodeId, container.id)}
                className="flex w-full flex-col rounded px-1 py-0.5 text-left hover:bg-accent"
              >
                <span className="truncate text-xs font-medium" title={container.title}>
                  {container.title}
                </span>
                <span className="text-[11px] text-muted-foreground">
                  {t('analysis.overview.clusterSize', { count: container.nodeIds.length })}
                  {risks > 0 && ` · ${t('analysis.overview.clusterRisks', { count: risks })}`}
                </span>

                {/* Requirement 7's per-container half. Same set as the overall bar above. */}
                <ReviewProgress nodeIds={container.nodeIds} className="mt-0.5 text-[11px]" />
              </button>
            </li>
          );
        })}
      </ol>
    </section>
  );
}

/**
 * Where this analysis came from and what it cost.
 *
 * Cost is absent, never zero, when the model was not in the price table: an unrated model cost an
 * unknown amount, and reporting nothing spent would be a claim the application cannot make.
 */
function RunMetadata({ view }: { view: AnalysisView }) {
  const t = useT();
  const unknown = t('analysis.overview.runUnknown');

  return (
    <section className="flex min-w-0 flex-col gap-2">
      <h3 className="text-sm font-semibold">{t('analysis.overview.runHeading')}</h3>

      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        <Entry label={t('analysis.overview.runProvider')} value={view.providerDisplayName ?? unknown} />
        <Entry label={t('analysis.overview.runModel')} value={view.model ?? unknown} />
        <Entry
          label={t('analysis.overview.runWhen')}
          value={view.createdAtUtc ? new Date(view.createdAtUtc).toLocaleString() : unknown}
        />
        <Entry
          label={t('analysis.overview.runCommit')}
          value={view.headCommit ? view.headCommit.slice(0, 8) : unknown}
        />
        <Entry
          label={t('analysis.overview.runTokensIn')}
          value={formatCount(view.inputTokens ?? 0)}
        />
        <Entry
          label={t('analysis.overview.runTokensOut')}
          value={formatCount(view.outputTokens ?? 0)}
        />
        <Entry
          label={t('analysis.overview.runCost')}
          value={
            view.costUsd === undefined
              ? t('analysis.costUnknown')
              : t('analysis.cost', { cost: Number(view.costUsd).toFixed(4) })
          }
        />
        <Entry
          label={t('analysis.overview.runDuration')}
          value={t('analysis.overview.runSeconds', {
            count: Math.round((view.durationMs ?? 0) / 1000),
          })}
        />
        <Entry
          label={t('analysis.overview.runRepairs')}
          value={formatCount(view.repairRounds ?? 0)}
        />
        <Entry label={t('analysis.overview.runSchema')} value={view.schemaVersion ?? unknown} />
      </dl>
    </section>
  );
}

function Entry({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="truncate font-medium" title={value}>
        {value}
      </dd>
    </div>
  );
}

function Diagnostics({ view }: { view: AnalysisView }) {
  const t = useT();

  if (view.diagnostics.length === 0) return null;

  return (
    <section className="flex min-w-0 flex-col gap-2">
      <h3 className="text-sm font-semibold">{t('analysis.diagnosticsHeading')}</h3>
      <p className="text-xs text-muted-foreground">{t('analysis.diagnosticsBody')}</p>

      <ul className="flex flex-col gap-2 text-sm">
        {view.diagnostics.map((diagnostic, index) => (
          <li key={`${diagnostic.code}-${index}`} className="flex flex-col">
            <Diagnostic code={diagnostic.code} />
            {diagnostic.subject && (
              <span className="break-all font-mono text-xs text-muted-foreground">
                {diagnostic.subject}
              </span>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

/**
 * Iteration 7's long-form result.
 *
 * Still here, still folded, and now folded one level deeper — the diagram plus its hover cards say
 * the same thing in a form that fits on one screen, but this is the only place the model's whole
 * answer can be read continuously, and it is what a reviewer falls back to when the graph itself is
 * the confusing thing.
 */
function DetailsDisclosure({ view }: { view: AnalysisView }) {
  const t = useT();
  const open = useAppStore((state) => state.graphDetailsOpen);
  const setOpen = useAppStore((state) => state.setGraphDetailsOpen);

  return (
    <section>
      <button
        type="button"
        onClick={() => setOpen(!open)}
        aria-expanded={open}
        className="flex w-full items-center gap-1.5 text-left text-sm font-semibold hover:text-primary"
      >
        {open ? (
          <ChevronDown className="size-4" aria-hidden />
        ) : (
          <ChevronRight className="size-4" aria-hidden />
        )}
        {t('analysis.detailsHeading')}
      </button>

      {!open && <p className="mt-1 pl-5 text-xs text-muted-foreground">{t('analysis.detailsBody')}</p>}

      {open && <Details view={view} />}
    </section>
  );
}

/** Iteration 7's long-form result, unchanged in substance. */
function Details({ view }: { view: AnalysisView }) {
  const t = useT();
  const nodesById = new Map(view.nodes.map((node) => [node.id, node]));

  return (
    <div className="mt-4 flex flex-col gap-6">
      <section className="flex flex-col gap-6">
        <div>
          <h3 className="text-sm font-medium">{t('analysis.containersHeading')}</h3>
          <p className="text-xs text-muted-foreground">{t('analysis.containersDescription')}</p>
        </div>

        {view.containers.map((container, index) => (
          <section key={container.id} className="flex flex-col gap-3">
            <header className="flex flex-col gap-1">
              <p className="text-xs text-muted-foreground">
                {t('analysis.containerOrder', { order: index + 1, total: view.containers.length })}
                {' · '}
                {t('analysis.nodeCount', { count: container.nodeIds.length })}
              </p>
              <h4 className="text-base font-medium">{container.title}</h4>
              <Markdown text={container.summary} className="text-sm text-muted-foreground" />
            </header>

            <Markdown text={container.explanation} className="text-sm leading-relaxed" />
            <RiskList risks={container.risks} hideWhenEmpty />

            <ol className="grid gap-3 lg:grid-cols-2">
              {container.nodeIds
                .map((id) => nodesById.get(id))
                .filter((node): node is AnalysisNodeInfo => node !== undefined)
                .map((node) => (
                  <li key={node.id}>
                    <NodeCard node={node} isEntry={node.id === container.entryNodeId} />
                  </li>
                ))}
            </ol>
          </section>
        ))}
      </section>

      <section className="flex flex-col gap-3">
        <h3 className="text-sm font-medium">{t('analysis.edgesHeading')}</h3>

        {view.edges.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('analysis.noEdges')}</p>
        ) : (
          <ul className="flex flex-col gap-3">
            {view.edges.map((edge) => (
              <li
                key={`${edge.sourceNodeId}->${edge.targetNodeId}`}
                className="flex flex-col gap-1 text-sm"
              >
                <p className="flex flex-wrap items-center gap-2 font-mono text-xs">
                  <span>{edge.sourceNodeId}</span>
                  <span aria-hidden>→</span>
                  <span>{edge.targetNodeId}</span>
                  <Badge variant={edge.kind === 'direct' ? 'secondary' : 'outline'}>
                    {t(edge.kind === 'direct' ? 'analysis.edgeDirect' : 'analysis.edgeConceptual')}
                  </Badge>
                  {edge.crossesContainers && (
                    <Badge variant="outline">{t('analysis.edgeCrosses')}</Badge>
                  )}
                </p>
                <Markdown text={edge.explanation} className="text-muted-foreground" />
                <RiskList risks={edge.risks} hideWhenEmpty />
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

/** One file, or one place inside one. */
function NodeCard({ node, isEntry }: { node: AnalysisNodeInfo; isEntry: boolean }) {
  const t = useT();

  return (
    <article className="h-full rounded-md border p-3">
      <header className="flex flex-wrap items-center gap-2">
        {isEntry && <Badge>{t('analysis.entryNode')}</Badge>}
        <span className="break-all font-mono text-xs">{node.filePath}</span>
        {node.symbol && <span className="text-xs text-muted-foreground">{node.symbol}</span>}
        {node.startLine > 0 && (
          <span className="text-xs text-muted-foreground">
            {t('analysis.nodeLines', { start: node.startLine, end: node.endLine })}
          </span>
        )}
        {node.states.map((state) => (
          <Badge key={state} variant={state === 'risky' ? 'destructive' : 'outline'}>
            {t(stateLabels[state])}
          </Badge>
        ))}
        <span className="ml-auto text-xs text-muted-foreground">
          {t('analysis.nodeImportance', { value: node.importance })}
        </span>
      </header>

      <h5 className="mt-2 text-sm font-medium">{node.title}</h5>

      <div className="mt-2 flex flex-col gap-3 text-sm">
        <dl className="flex flex-col gap-2">
          <Field label={t('analysis.nodeWhatChanged')} value={node.whatChanged} />
          <Field label={t('analysis.nodeWhyItChanged')} value={node.whyItChanged} />
          <Field label={t('analysis.nodeAffects')} value={node.howItAffectsOthers} />
          <Field label={t('analysis.nodeNotes')} value={node.implementationNotes} />
        </dl>

        <RiskList risks={node.risks} hideWhenEmpty />
      </div>
    </article>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  if (!value) return null;

  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd>
        <Markdown text={value} />
      </dd>
    </div>
  );
}

/** The deterministic half: numbers the application counted rather than the model reported. */
function Statistics({ view }: { view: AnalysisView }) {
  const t = useT();
  const stats = view.statistics;

  if (!stats) return null;

  return (
    <section className="flex min-w-0 flex-col gap-2">
      <h3 className="text-sm font-semibold">{t('analysis.statisticsHeading')}</h3>

      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        <Stat label={t('analysis.statFiles')} value={formatCount(stats.totalFiles)} />
        <Stat
          label={t('analysis.statLines')}
          value={`+${formatCount(stats.totalLinesAdded)} −${formatCount(stats.totalLinesRemoved)}`}
        />
        <Stat label={t('analysis.statNodes')} value={formatCount(stats.nodeCount)} />
        <Stat label={t('analysis.statContainers')} value={formatCount(stats.containerCount)} />
        <Stat
          label={t('analysis.statEdges')}
          value={formatCount(stats.edgeCount)}
          hint={t('analysis.statEdgeSplit', {
            direct: stats.directEdgeCount,
            conceptual: stats.conceptualEdgeCount,
          })}
        />
        <Stat label={t('analysis.statLongestChain')} value={formatCount(stats.longestChain)} />
        <Stat
          label={t('analysis.statFanIn')}
          value={stats.highestFanInNodeId ?? t('analysis.statNone')}
        />
        <Stat
          label={t('analysis.statFanOut')}
          value={stats.highestFanOutNodeId ?? t('analysis.statNone')}
        />
        <Stat label={t('analysis.statRiskyNodes')} value={formatCount(stats.riskyNodeCount)} />
        <Stat label={t('analysis.statRisks')} value={formatCount(stats.riskCount)} />
      </dl>

      <p className="text-xs text-muted-foreground">
        {stats.languages.join(', ')}
        {stats.languages.length > 0 && stats.projects.length > 0 && ' · '}
        {stats.projects.join(', ')}
      </p>
    </section>
  );
}

function Stat({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="truncate font-medium" title={value}>
        {value}
      </dd>
      {hint && <dd className="text-xs text-muted-foreground">{hint}</dd>}
    </div>
  );
}

/**
 * Diagnostics arrive as codes, not prose, so the wording is the renderer's.
 *
 * Only warnings ever reach a stored analysis — an error would have failed the run — so this covers
 * the warning codes, and anything else renders as the code itself rather than as the host's
 * developer text.
 */
function Diagnostic({ code }: { code: string }) {
  const t = useT();
  const key = diagnosticLabels[code];

  return <span>{key ? t(key) : code}</span>;
}

const diagnosticLabels: Record<string, ResourceKey> = {
  cycle: 'analysis.diagnostic.cycle',
  self_edge: 'analysis.diagnostic.self_edge',
  incomplete_reading_order: 'analysis.diagnostic.incomplete_reading_order',
  unreachable_node: 'analysis.diagnostic.unreachable_node',
  verbose_field: 'analysis.diagnostic.verbose_field',
  implementation_group_split: 'analysis.diagnostic.implementation_group_split',
};
