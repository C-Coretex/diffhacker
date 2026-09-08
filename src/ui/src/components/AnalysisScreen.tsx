import { useCallback, useEffect, useRef, useState } from 'react';
import { Loader2Icon, NetworkIcon, TriangleAlertIcon } from 'lucide-react';
import type { AnalysisNodeInfo, AnalysisNodeInfoState, AnalysisView } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { formatCount } from '@/i18n/format';
import type { ResourceKey } from '@/i18n/translate';
import { useT } from '@/i18n/useT';
import { getAnalysis, onAnalysisProgress, onToolCallEvent, runAnalysis } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Separator } from '@/components/ui/separator';
import { AnalysisRunPanel } from './ProfileRunPanel';

const stateLabels = {
  changed: 'analysis.state.changed',
  added: 'analysis.state.added',
  deleted: 'analysis.state.deleted',
  unchanged_relevant: 'analysis.state.unchanged_relevant',
  risky: 'analysis.state.risky',
  entry_point: 'analysis.state.entry_point',
} as const satisfies Record<AnalysisNodeInfoState, string>;

/**
 * The analysed change.
 *
 * Not a diagram — that is Iteration 8. What this screen proves is that the result exists and is
 * complete: every cluster in the order the model put them, every file inside one in the order it
 * ranked them, the relationships between them, and the risks in a column of their own rather than
 * mixed into the prose.
 *
 * Nothing appears until the whole run has finished (§0.2.8). While one is going, the only thing on
 * screen is the live panel and a way to stop it.
 */
export function AnalysisScreen() {
  const t = useT();
  const client = useRpc();

  const repository = useAppStore((state) => state.repositoryInfo);
  const status = useAppStore((state) => state.analysis);
  const view = useAppStore((state) => state.analysisView);
  const error = useAppStore((state) => state.analysisError);
  const run = useAppStore((state) => state.analysisRun);

  const startLoading = useAppStore((store) => store.startLoadingAnalysis);
  const setAnalysis = useAppStore((store) => store.setAnalysis);
  const failAnalysis = useAppStore((store) => store.failAnalysis);
  const startRun = useAppStore((store) => store.startAnalysisRun);
  const endRun = useAppStore((store) => store.endAnalysisRun);
  const recordProgress = useAppStore((store) => store.recordAnalysisProgress);
  const recordEvent = useAppStore((store) => store.recordAnalysisRunEvent);

  const [runError, setRunError] = useState<string>();
  const abort = useRef<AbortController>(null);
  const path = repository?.path;

  // Reading the stored analysis never starts a conversation, which is the whole reason it is
  // stored: the money was spent once.
  useEffect(() => {
    if (!client || !path || status !== 'idle') return;

    startLoading();

    getAnalysis(client, { repositoryPath: path })
      .then(setAnalysis)
      .catch((caught: unknown) => failAnalysis(describeError(caught)));
  }, [client, path, status, startLoading, setAnalysis, failAnalysis]);

  // Subscribed for the life of the screen rather than for the life of a run: a notification that
  // arrives a moment after the call resolves still belongs to the log.
  useEffect(() => {
    if (!client) return;

    const stopProgress = onAnalysisProgress(client, recordProgress);
    const stopEvents = onToolCallEvent(client, recordEvent);

    return () => {
      stopProgress();
      stopEvents();
    };
  }, [client, recordProgress, recordEvent]);

  const analyse = useCallback(async () => {
    if (!client || !path) return;

    const controller = new AbortController();
    abort.current = controller;

    setRunError(undefined);
    startRun();

    try {
      setAnalysis(await runAnalysis(client, { repositoryPath: path }, controller.signal));
    } catch (caught) {
      // A cancelled run is not a failure to report as one, but it did spend money, so the message
      // says what happened rather than pretending nothing did.
      setRunError(controller.signal.aborted ? t('analysis.cancelled') : describeError(caught));
    } finally {
      abort.current = null;
      endRun();
    }
  }, [client, path, startRun, setAnalysis, endRun, t]);

  if (!repository) return null;

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="flex flex-col gap-1.5">
            <CardTitle className="flex items-center gap-2">
              <NetworkIcon className="size-4" aria-hidden />
              {t('analysis.heading')}
            </CardTitle>
            <CardDescription>{t('analysis.description')}</CardDescription>
          </div>

          <Button disabled={run === 'running'} onClick={() => void analyse()}>
            {run === 'running' ? (
              <Loader2Icon className="size-4 animate-spin" aria-hidden />
            ) : (
              <NetworkIcon aria-hidden />
            )}
            {run === 'running'
              ? t('analysis.running')
              : view?.hasAnalysis
                ? t('analysis.rerun')
                : t('analysis.run')}
          </Button>
        </CardHeader>

        {view?.hasAnalysis && run !== 'running' && (
          <CardContent>
            <Provenance view={view} />
          </CardContent>
        )}
      </Card>

      {/*
        role="alert" rather than the plain card the Alert primitive gives: a run that failed did so
        because the reviewer asked for it, and it is the one thing on this screen a screen reader
        should be told about without being asked.
      */}
      {runError && (
        <Alert variant="destructive" role="alert">
          <TriangleAlertIcon aria-hidden />
          <AlertTitle>{t('analysis.heading')}</AlertTitle>
          <AlertDescription className="whitespace-pre-wrap">{runError}</AlertDescription>
        </Alert>
      )}

      {error && status === 'error' && (
        <Alert variant="destructive" role="alert">
          <TriangleAlertIcon aria-hidden />
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}

      {run === 'running' && <AnalysisRunPanel onCancel={() => abort.current?.abort()} />}

      {run !== 'running' &&
        (view?.hasAnalysis ? (
          <Result view={view} />
        ) : (
          status === 'ready' && (
            <Card>
              <CardHeader>
                <CardTitle>{t('analysis.emptyHeading')}</CardTitle>
                <CardDescription>{t('analysis.emptyBody')}</CardDescription>
              </CardHeader>
            </Card>
          )
        ))}
    </div>
  );
}

/** Where this analysis came from and what it cost. */
function Provenance({ view }: { view: AnalysisView }) {
  const t = useT();

  const date = view.createdAtUtc ? new Date(view.createdAtUtc).toLocaleString() : '';
  const duration = `${Math.round((view.durationMs ?? 0) / 1000)}s`;

  return (
    <div className="text-muted-foreground flex flex-wrap items-center gap-x-4 gap-y-1 text-xs">
      <span>
        {view.headCommit
          ? t('analysis.provenanceCommit', {
              date,
              commit: view.headCommit.slice(0, 8),
              model: view.model ?? '',
              duration,
            })
          : t('analysis.provenance', { date, model: view.model ?? '', duration })}
      </span>
      <span>
        {t('analysis.usage', {
          input: formatCount(view.inputTokens ?? 0),
          output: formatCount(view.outputTokens ?? 0),
        })}
      </span>
      <span>
        {view.costUsd === undefined
          ? t('analysis.costUnknown')
          : t('analysis.cost', { cost: Number(view.costUsd).toFixed(4) })}
      </span>
      {(view.repairRounds ?? 0) > 0 && (
        <span>{t('analysis.repairs', { count: view.repairRounds ?? 0 })}</span>
      )}
    </div>
  );
}

/** The whole result, in the order a reviewer walks it. */
function Result({ view }: { view: AnalysisView }) {
  const t = useT();
  const nodesById = new Map(view.nodes.map((node) => [node.id, node]));

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader>
          <CardTitle>{t('analysis.summaryHeading')}</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-6 md:grid-cols-[2fr_1fr]">
          <p className="text-sm leading-relaxed whitespace-pre-wrap">{view.summary}</p>

          {/*
            Risks in a column beside the explanation, never inside it. §0.2 keeps the two apart all
            the way from the schema to here, and this is where that finally shows.
          */}
          <section className="flex flex-col gap-2">
            <h3 className="text-sm font-medium">{t('analysis.risksHeading')}</h3>
            <RiskList risks={view.overallRisks} />
          </section>
        </CardContent>
      </Card>

      <Statistics view={view} />

      <Card>
        <CardHeader>
          <CardTitle>{t('analysis.containersHeading')}</CardTitle>
          <CardDescription>{t('analysis.containersDescription')}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-8">
          {view.containers.map((container, index) => (
            <section key={container.id} className="flex flex-col gap-3">
              <header className="flex flex-col gap-1">
                <p className="text-muted-foreground text-xs">
                  {t('analysis.containerOrder', {
                    order: index + 1,
                    total: view.containers.length,
                  })}
                  {' · '}
                  {t('analysis.nodeCount', { count: container.nodeIds.length })}
                </p>
                <h3 className="text-base font-medium">{container.title}</h3>
                <p className="text-muted-foreground text-sm">{container.summary}</p>
              </header>

              <div className="grid gap-4 md:grid-cols-[2fr_1fr]">
                <p className="text-sm leading-relaxed whitespace-pre-wrap">
                  {container.explanation}
                </p>
                <RiskList risks={container.risks} />
              </div>

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
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t('analysis.edgesHeading')}</CardTitle>
        </CardHeader>
        <CardContent>
          {view.edges.length === 0 ? (
            <p className="text-muted-foreground text-sm">{t('analysis.noEdges')}</p>
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
        </CardContent>
      </Card>

      {view.diagnostics.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle>{t('analysis.diagnosticsHeading')}</CardTitle>
            <CardDescription>{t('analysis.diagnosticsBody')}</CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="flex flex-col gap-2 text-sm">
              {view.diagnostics.map((diagnostic, index) => (
                <li key={`${diagnostic.code}-${index}`} className="flex flex-col">
                  <Diagnostic code={diagnostic.code} />
                  {diagnostic.subject && (
                    <span className="text-muted-foreground font-mono text-xs">
                      {diagnostic.subject}
                    </span>
                  )}
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}
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
        <span className="font-mono text-xs break-all">{node.filePath}</span>
        {node.symbol && <span className="text-muted-foreground text-xs">{node.symbol}</span>}
        {node.startLine > 0 && (
          <span className="text-muted-foreground text-xs">
            {t('analysis.nodeLines', { start: node.startLine, end: node.endLine })}
          </span>
        )}
        {node.states.map((state) => (
          <Badge key={state} variant={state === 'risky' ? 'destructive' : 'outline'}>
            {t(stateLabels[state])}
          </Badge>
        ))}
        <span className="text-muted-foreground ml-auto text-xs">
          {t('analysis.nodeImportance', { value: node.importance })}
        </span>
      </header>

      <h4 className="mt-2 text-sm font-medium">{node.title}</h4>

      <div className="mt-2 grid gap-3 text-sm md:grid-cols-[2fr_1fr]">
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
      <dt className="text-muted-foreground text-xs">{label}</dt>
      <dd className="whitespace-pre-wrap">{value}</dd>
    </div>
  );
}

function RiskList({ risks, hideWhenEmpty }: { risks: readonly string[]; hideWhenEmpty?: boolean }) {
  const t = useT();

  if (risks.length === 0) {
    return hideWhenEmpty ? null : (
      <p className="text-muted-foreground text-sm">{t('analysis.noRisks')}</p>
    );
  }

  return (
    <ul className="flex flex-col gap-1.5">
      {risks.map((risk, index) => (
        <li key={index} className="flex gap-2 text-sm">
          <TriangleAlertIcon className="text-warning-foreground mt-0.5 size-3.5 shrink-0" aria-hidden />
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
    <Card>
      <CardHeader>
        <CardTitle>{t('analysis.statisticsHeading')}</CardTitle>
      </CardHeader>
      <CardContent>
        <dl className="grid grid-cols-2 gap-4 text-sm sm:grid-cols-3 lg:grid-cols-5">
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

        <Separator className="my-4" />

        <p className="text-muted-foreground text-xs">
          {stats.languages.join(', ')}
          {stats.languages.length > 0 && stats.projects.length > 0 && ' · '}
          {stats.projects.join(', ')}
        </p>
      </CardContent>
    </Card>
  );
}

function Stat({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="flex flex-col">
      <dt className="text-muted-foreground text-xs">{label}</dt>
      <dd className="font-medium break-all">{value}</dd>
      {hint && <dd className="text-muted-foreground text-xs">{hint}</dd>}
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
};
