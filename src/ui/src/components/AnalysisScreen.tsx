import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { Loader2Icon, NetworkIcon, TriangleAlertIcon, XIcon } from 'lucide-react';
import type { AnalysisView } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { formatCount } from '@/i18n/format';
import { useT } from '@/i18n/useT';
import { getAnalysis, onAnalysisProgress, onToolCallEvent, runAnalysis } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { AnalysisOverviewBand } from './AnalysisOverviewBand';
import { useReviewShortcuts } from './diff/useReviewShortcuts';
import { useSplitter } from './diff/useSplitter';
import { AnalysisGraph } from './graph/AnalysisGraph';
import { AnalysisRunPanel } from './ProfileRunPanel';

/**
 * The analysed change, as a diagram.
 *
 * ```
 * ┌────────────────────────────────────────────────────────────┐
 * │ run controls · model · tokens · cost · duration            │
 * ├──────────────────────────────────┬─────────────────────────┤
 * │ SUMMARY                          │ ⚠ RISKS                 │
 * │ ▸ Overview  (order, clusters, numbers, every risk, run)    │
 * ├───────────────────────────────────────────┬────────────────┤
 * │ [search] [fit] [collapse all]    [legend] ║  diff panel    │
 * ├───────────────────────────────────────────╢  Monaco, the   │
 * │                                           ║  explanation,  │
 * │              G R A P H         ┌────────┐ ║  where to go   │
 * └───────────────────────────────┴────────┴──╨────────────────┘
 * ```
 *
 * Iteration 8 put the summary in a left rail. Iteration 9 moved it across the top, which is what
 * makes the two things after it possible: the diagram gets the whole width it wants at three
 * hundred boxes, and Iteration 10's diff panel — which can expand to full width — has a side of
 * the screen to expand into without arguing with a rail for it.
 *
 * The divider between the two is draggable, and its travel stops short of the left edge: requirement
 * 10 asks that the reviewer's position stay visible **in the graph** at all times, "including while
 * the diff panel is expanded to full width", so the widest the panel goes still leaves the diagram a
 * rail — and the rail stays centred on whatever the panel is showing. Full screen is the separate,
 * explicit mode beside that: asked for by name, entered and left by one button, and the diagram is
 * hidden rather than thrown away.
 *
 * The screen owns its own scrolling: the band scrolls inside itself, the diagram never does — a
 * canvas inside a scrolling page is one the reviewer scrolls past instead of panning. `App.tsx`
 * gives this screen the window with no padding for exactly that reason.
 *
 * Nothing of the result appears until the whole run has finished (§0.2.8). While one is going, the
 * only thing on screen is the live panel and a way to stop it.
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

  const analysed = view?.hasAnalysis === true && run !== 'running';

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="flex shrink-0 flex-wrap items-center gap-3 border-b border-border px-6 py-3">
        <div className="flex min-w-0 flex-col">
          <h1 className="flex items-center gap-2 text-sm font-semibold">
            <NetworkIcon className="size-4" aria-hidden />
            {t('analysis.heading')}
          </h1>
          {analysed ? (
            <Provenance view={view} />
          ) : (
            <p className="text-xs text-muted-foreground">{t('analysis.description')}</p>
          )}
        </div>

        <Button
          className="ml-auto"
          disabled={run === 'running'}
          onClick={() => void analyse()}
        >
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
      </header>

      {(runError || (error && status === 'error')) && (
        <div className="shrink-0 px-6 py-3">
          {/*
            role="alert" rather than the plain card the Alert primitive gives: a run that failed did
            so because the reviewer asked for it, and it is the one thing on this screen a screen
            reader should be told about without being asked.
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
        </div>
      )}

      {run === 'running' && (
        <div className="min-h-0 flex-1 overflow-auto px-6 py-4">
          <AnalysisRunPanel onCancel={() => abort.current?.abort()} />
        </div>
      )}

      {analysed && view && (
        <div className="flex min-h-0 flex-1 flex-col">
          <AnalysisOverviewBand view={view} />
          <ReviewWorkspace view={view} />
        </div>
      )}

      {!analysed && run !== 'running' && status === 'ready' && (
        <div className="min-h-0 flex-1 overflow-auto px-6 py-4">
          <Card>
            <CardHeader>
              <CardTitle>{t('analysis.emptyHeading')}</CardTitle>
              <CardDescription>{t('analysis.emptyBody')}</CardDescription>
            </CardHeader>
          </Card>
        </div>
      )}
    </div>
  );
}

/**
 * The diff panel, and Monaco with it, as a chunk of its own.
 *
 * Monaco is by a wide margin the largest thing in this bundle — several megabytes against a few
 * hundred kilobytes for everything else — and a static import would put all of it in the first script
 * the window parses, before the welcome screen has drawn. Loading it when a reviewer first opens a
 * file costs a moment they asked for; loading it at startup costs one nobody did.
 *
 * The chunk is fetched from `diffhacker://app/assets/`, in-process like every other asset. No network
 * is involved and no CDN is reachable — `connect-src 'self'` sees to that (§0.2.13).
 */
const DiffPanel = lazy(async () => ({ default: (await import('./diff/DiffPanel')).DiffPanel }));

/**
 * The diagram and the diff, side by side, with a divider between them.
 *
 * The panel is mounted only while a node is open. Creating an editor is not free either, so a
 * reviewer who has not opened a file has paid for neither the code nor the editor.
 */
function ReviewWorkspace({ view }: { view: AnalysisView }) {
  const t = useT();
  const container = useRef<HTMLDivElement>(null);

  const nodeId = useAppStore((state) => state.diffNodeId);
  const width = useAppStore((state) => state.diffPanelWidth);
  const setWidth = useAppStore((state) => state.setDiffPanelWidth);
  const fullScreen = useAppStore((state) => state.diffFullScreen);

  const splitter = useSplitter(container, width, setWidth);

  useReviewShortcuts(view);

  const open = nodeId !== undefined;
  const expanded = open && fullScreen;

  return (
    <div ref={container} className="relative flex min-h-0 flex-1">
      {/*
        Hidden rather than unmounted in full screen. The diagram owns an ELK worker and a laid-out
        canvas that cost real time to build; tearing them down because the panel was maximised would
        make leaving full screen slower than entering it, for a diagram nobody stopped needing.
      */}
      <div className={expanded ? 'hidden' : 'min-h-0 min-w-0 flex-1'}>
        <AnalysisGraph view={view} />
      </div>

      {open && (
        <>
          {/*
            A separator rather than a button: it has no activated state and no action, it only moves.
            Keyboard resizing is not here, and the panel is fully usable without it — §0.6 puts full
            keyboard control in a later piece of work rather than half of it in this one.

            Gone in full screen, because there is nothing on the other side of it to divide.
          */}
          {!expanded && (
            <div
              role="separator"
              aria-orientation="vertical"
              aria-label={t('analysis.diff.resizeHandle')}
              data-testid="diff-splitter"
              data-dragging={splitter.dragging ? 'true' : 'false'}
              onPointerDown={splitter.onPointerDown}
              className={
                splitter.dragging
                  ? 'w-1 shrink-0 cursor-col-resize bg-primary'
                  : 'w-1 shrink-0 cursor-col-resize bg-border hover:bg-primary/60'
              }
            />
          )}

          <div
            className={
              expanded
                ? 'min-h-0 min-w-0 flex-1'
                : 'min-h-0 shrink-0 border-l border-border'
            }
            style={expanded ? undefined : { width }}
          >
            <Suspense
              fallback={
                <p className="flex h-full items-center justify-center text-sm text-muted-foreground">
                  {t('analysis.diff.loading')}
                </p>
              }
            >
              <DiffPanel view={view} />
            </Suspense>
          </div>
        </>
      )}

      <EditorFailure />
    </div>
  );
}

/**
 * The one place an external editor's failure is reported.
 *
 * The buttons that ask for one are on two surfaces — the boxes on the diagram and the panel's header
 * — and a 260-pixel box has nowhere to put a sentence. So the message is store state and it is drawn
 * once, over the workspace, wherever the request came from. Verification step 9 is exactly this: a
 * renamed `code` gives a clear message and no crash.
 */
function EditorFailure() {
  const t = useT();
  const message = useAppStore((state) => state.editorError);
  const dismiss = useAppStore((state) => state.setEditorError);

  if (!message) return null;

  return (
    <div
      role="alert"
      data-testid="editor-error"
      className="absolute bottom-4 left-1/2 z-20 flex max-w-xl -translate-x-1/2 items-start gap-3 rounded-md border border-destructive bg-card px-3 py-2 shadow-lg"
    >
      <TriangleAlertIcon className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden />
      <p className="text-xs text-destructive">{message}</p>

      <button
        type="button"
        onClick={() => dismiss(undefined)}
        aria-label={t('analysis.diff.dismissEditorError')}
        className="shrink-0 rounded p-0.5 text-muted-foreground hover:bg-accent hover:text-foreground"
      >
        <XIcon className="size-3.5" aria-hidden />
      </button>
    </div>
  );
}

/** Where this analysis came from and what it cost. */
function Provenance({ view }: { view: AnalysisView }) {
  const t = useT();

  const date = view.createdAtUtc ? new Date(view.createdAtUtc).toLocaleString() : '';
  const duration = `${Math.round((view.durationMs ?? 0) / 1000)}s`;

  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
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
