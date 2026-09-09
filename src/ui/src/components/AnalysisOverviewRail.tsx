import { ChevronDown, ChevronRight, TriangleAlertIcon } from 'lucide-react';
import type { AnalysisNodeInfo, AnalysisNodeInfoState, AnalysisView } from '@/contracts';
import { formatCount } from '@/i18n/format';
import type { ResourceKey } from '@/i18n/translate';
import { useT } from '@/i18n/useT';
import { Badge } from '@/components/ui/badge';
import { Separator } from '@/components/ui/separator';
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
 * Everything about the analysed change that is not the diagram: what it does as a whole, what it
 * risks, what the application counted, where it came from, and what validation noticed.
 *
 * This was the whole of Iteration 7's screen. It is a rail now because Iteration 8 made the diagram
 * the main thing — but the prose did not stop being useful, and a graph of three hundred boxes with
 * no summary beside it is a puzzle rather than an answer.
 *
 * The long-form lists Iteration 7 rendered — every cluster, every node with its four prose fields,
 * every edge with its explanation — live behind a **Details** disclosure. Folded by default because
 * the diagram now says the same thing in a form that fits on one screen; kept because it is the
 * only place the model's full text can be read, and because it is what a reviewer falls back to
 * when the graph is the thing that is confusing.
 */
export function AnalysisOverviewRail({ view }: { view: AnalysisView }) {
  const t = useT();
  const detailsOpen = useAppStore((state) => state.graphDetailsOpen);
  const setDetailsOpen = useAppStore((state) => state.setGraphDetailsOpen);

  return (
    <div className="flex h-full min-h-0 flex-col overflow-y-auto border-r border-border">
      <div className="flex flex-col gap-6 p-4">
        <section className="flex flex-col gap-2">
          <h2 className="text-sm font-semibold">{t('analysis.summaryHeading')}</h2>
          <p className="whitespace-pre-wrap text-sm leading-relaxed">{view.summary}</p>
        </section>

        {/*
          Risks in a section of their own, never inside the summary. §0.2 keeps the two apart all
          the way from the schema to here, and this is where that finally shows.
        */}
        <section className="flex flex-col gap-2">
          <h2 className="text-sm font-semibold">{t('analysis.risksHeading')}</h2>
          <RiskList risks={view.overallRisks} />
        </section>

        <Statistics view={view} />

        {view.diagnostics.length > 0 && (
          <section className="flex flex-col gap-2">
            <h2 className="text-sm font-semibold">{t('analysis.diagnosticsHeading')}</h2>
            <p className="text-xs text-muted-foreground">{t('analysis.diagnosticsBody')}</p>
            <ul className="flex flex-col gap-2 text-sm">
              {view.diagnostics.map((diagnostic, index) => (
                <li key={`${diagnostic.code}-${index}`} className="flex flex-col">
                  <Diagnostic code={diagnostic.code} />
                  {diagnostic.subject && (
                    <span className="font-mono text-xs text-muted-foreground">
                      {diagnostic.subject}
                    </span>
                  )}
                </li>
              ))}
            </ul>
          </section>
        )}

        <Separator />

        <section>
          <button
            type="button"
            onClick={() => setDetailsOpen(!detailsOpen)}
            aria-expanded={detailsOpen}
            className="flex w-full items-center gap-1.5 text-left text-sm font-semibold hover:text-primary"
          >
            {detailsOpen ? (
              <ChevronDown className="size-4" aria-hidden />
            ) : (
              <ChevronRight className="size-4" aria-hidden />
            )}
            {t('analysis.detailsHeading')}
          </button>

          {!detailsOpen && (
            <p className="mt-1 pl-5 text-xs text-muted-foreground">{t('analysis.detailsBody')}</p>
          )}

          {detailsOpen && <Details view={view} />}
        </section>
      </div>
    </div>
  );
}

/** Iteration 7's long-form result, unchanged in substance and narrower in column. */
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
              <p className="text-sm text-muted-foreground">{container.summary}</p>
            </header>

            <p className="whitespace-pre-wrap text-sm leading-relaxed">{container.explanation}</p>
            <RiskList risks={container.risks} hideWhenEmpty />

            <ol className="flex flex-col gap-3">
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
                <p className="text-muted-foreground">{edge.explanation}</p>
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
    <article className="rounded-md border p-3">
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
      <dd className="whitespace-pre-wrap">{value}</dd>
    </div>
  );
}

export function RiskList({
  risks,
  hideWhenEmpty,
}: {
  risks: readonly string[];
  hideWhenEmpty?: boolean;
}) {
  const t = useT();

  if (risks.length === 0) {
    return hideWhenEmpty ? null : (
      <p className="text-sm text-muted-foreground">{t('analysis.noRisks')}</p>
    );
  }

  return (
    <ul className="flex flex-col gap-1.5">
      {risks.map((risk, index) => (
        <li key={index} className="flex gap-2 text-sm">
          <TriangleAlertIcon className="mt-0.5 size-3.5 shrink-0 text-warning-foreground" aria-hidden />
          <span>{risk}</span>
        </li>
      ))}
    </ul>
  );
}

/** The deterministic half: numbers the application counted rather than the model reported. */
function Statistics({ view }: { view: AnalysisView }) {
  const t = useT();
  const stats = view.statistics;

  if (!stats) return null;

  return (
    <section className="flex flex-col gap-2">
      <h2 className="text-sm font-semibold">{t('analysis.statisticsHeading')}</h2>

      <dl className="grid grid-cols-2 gap-3 text-sm">
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
    <div className="flex flex-col">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="break-all font-medium">{value}</dd>
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
};
