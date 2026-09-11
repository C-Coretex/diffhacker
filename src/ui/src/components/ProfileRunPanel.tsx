import { Loader2Icon } from 'lucide-react';
import type { AnalysisProgress, AnalysisProgressPhase, ToolCallEvent } from '@/contracts';
import type { ReactNode } from 'react';
import { formatCount, formatElapsed, formatTime } from '@/i18n/format';
import { useT } from '@/i18n/useT';
import { useElapsed } from '@/lib/useElapsed';
import { useAppStore } from '@/store/appStore';
import { ContextMeter } from './ContextMeter';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';

const phaseLabels = {
  exploring: 'progress.phase.exploring',
  analysing: 'progress.phase.analysing',
  grouping: 'progress.phase.grouping',
  explaining: 'progress.phase.explaining',
  finishing: 'progress.phase.finishing',
} as const satisfies Record<AnalysisProgressPhase, string>;

interface RunPanelProps {
  progress?: AnalysisProgress;
  events: readonly ToolCallEvent[];
  latest?: ToolCallEvent;
  /**
   * The last event that measured the context, which is usually not the last event at all — only
   * the turn-start and usage events carry a measurement.
   */
  context?: ToolCallEvent;
  /** When the run was started, by the renderer's clock. Drives the elapsed time. */
  startedAt?: number;
  onCancel(): void;
}

/**
 * The live view of a run.
 *
 * Two things at once, deliberately kept apart. The top line is what the model says it is doing, in
 * its own words, arriving through `analysis.progress`; the table below is the mechanical record of
 * what it actually did, arriving through `analysis.toolCall`. A reviewer reads the first and
 * consults the second, and interleaving them would ruin both.
 *
 * It takes its state as props rather than reading the store because there are two runs in the
 * application that look exactly like this — profiling a repository and analysing a change — and
 * they keep their own slices so that neither can show the other's log.
 */
export function RunPanel({ progress, events, latest, context, startedAt, onCancel }: RunPanelProps) {
  const t = useT();
  const elapsed = useElapsed(startedAt);

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-3">
        <CardTitle className="flex items-center gap-2">
          <Loader2Icon className="size-4 animate-spin" aria-hidden />
          {t('toolLog.heading')}
        </CardTitle>
        <Button size="sm" variant="outline" onClick={onCancel}>
          {t('profile.cancel')}
        </Button>
      </CardHeader>

      <CardContent className="flex flex-col gap-4">
        <div aria-live="polite" className="flex flex-col gap-1">
          {/*
            The message is the model's own words, shown as written. §0.6 keeps host-authored prose
            out of the host; this is run data, not copy, and translating it would be nonsense.
          */}
          <p className="text-sm">{progress?.message ?? t('toolLog.waiting')}</p>
        </div>

        {/*
          The live strip. Always drawn, from the first frame of the run: the clock and a zero are
          what tell a reviewer the run has started, and the first event can take a while. Every
          number but the clock is carried on the latest event as a running total, because the log
          below keeps only its most recent rows and could not be counted.
        */}
        <div className="text-muted-foreground flex flex-wrap items-center gap-x-6 gap-y-2 text-xs">
          <dl
            aria-label={t('toolLog.statsLabel')}
            data-testid="run-stats"
            className="flex flex-wrap items-start gap-x-6 gap-y-2"
          >
            {/* The phase is the model's, from report_progress; it is optional there, so a report
                that names none leaves the stage unstated rather than guessed. */}
            <Stat label={t('toolLog.phase')} testId="run-phase">
              {progress?.phase
                ? t(phaseLabels[progress.phase])
                : progress
                  ? t('toolLog.phaseUnstated')
                  : t('toolLog.phaseNone')}
            </Stat>
            <Stat label={t('toolLog.elapsed')} testId="run-elapsed">
              {formatElapsed(elapsed)}
            </Stat>
            <Stat label={t('toolLog.toolCalls')} testId="run-tool-calls">
              {formatCount(latest?.toolCallCount ?? 0)}
            </Stat>
            <Stat label={t('toolLog.turnLabel')} testId="run-turn">
              {formatCount(latest?.turn ?? 0)}
            </Stat>
            <Stat label={t('toolLog.tokensLabel')} testId="run-tokens">
              {t('toolLog.tokens', {
                input: formatCount(latest?.inputTokens ?? 0),
                output: formatCount(latest?.outputTokens ?? 0),
              })}
            </Stat>
            <Stat label={t('toolLog.costLabel')} testId="run-cost">
              {latest?.costUsd === undefined
                ? t('toolLog.costUnknown')
                : t('toolLog.cost', { cost: latest.costUsd.toFixed(4) })}
            </Stat>
          </dl>

          {/* Requirements 14 and 16. Mounted here rather than on the analysis screen so that
              profiling a repository gets the same meter for free — it is the same tool loop,
              filling the same context. */}
          {latest && <ContextMeter event={context} />}
        </div>

        <ToolLog events={events} />
      </CardContent>
    </Card>
  );
}

/** One figure on the live strip: a label above, the moving value below. */
function Stat({ label, testId, children }: { label: string; testId: string; children: ReactNode }) {
  return (
    <div className="flex flex-col">
      <dt>{label}</dt>
      <dd data-testid={testId} className="text-foreground font-mono tabular-nums">
        {children}
      </dd>
    </div>
  );
}

/** The profile run's slice of the store, in the shared panel. */
export function ProfileRunPanel({ onCancel }: { onCancel(): void }) {
  return (
    <RunPanel
      progress={useAppStore((state) => state.profileRunProgress)}
      events={useAppStore((state) => state.profileRunEvents)}
      latest={useAppStore((state) => state.profileRunLatest)}
      context={useAppStore((state) => state.profileRunContext)}
      startedAt={useAppStore((state) => state.profileRunStartedAt)}
      onCancel={onCancel}
    />
  );
}

/** The analysis run's slice, in the same panel. */
export function AnalysisRunPanel({ onCancel }: { onCancel(): void }) {
  return (
    <RunPanel
      progress={useAppStore((state) => state.analysisRunProgress)}
      events={useAppStore((state) => state.analysisRunEvents)}
      latest={useAppStore((state) => state.analysisRunLatest)}
      context={useAppStore((state) => state.analysisRunContext)}
      startedAt={useAppStore((state) => state.analysisRunStartedAt)}
      onCancel={onCancel}
    />
  );
}

/**
 * The tool log itself: tool calls, retries, and the model's own reasoning and reply text, as one
 * feed ordered newest first. Also used after a run, from the stored trace, which is why it takes
 * its events as a prop rather than reading the store.
 */
export function ToolLog({ events }: { events: readonly ToolCallEvent[] }) {
  const t = useT();

  if (events.length === 0) {
    return <p className="text-muted-foreground text-sm">{t('toolLog.empty')}</p>;
  }

  // Started and finished arrive as separate events; collapse shows one row per call, a finished
  // event replacing the started one it matches. The result is oldest first, so it is reversed
  // here — new entries belong at the top of a live log, not at the bottom of a scroll.
  const rows = [...collapse(events)].reverse();

  return (
    <ul className="max-h-80 overflow-auto rounded-md border text-xs">
      {rows.map((row) => (
        <li key={row.key} className="border-b px-3 py-2 last:border-b-0">
          {row.event.kind === 'assistant_message' ? (
            <AssistantMessageRow event={row.event} />
          ) : (
            <ToolCallRow event={row.event} />
          )}
        </li>
      ))}
    </ul>
  );
}

/** One tool call, retry, or turn-start row: the mechanical record of what the run did. */
function ToolCallRow({ event }: { event: ToolCallEvent }) {
  const t = useT();

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center justify-between gap-3">
        <span className="font-mono font-medium">
          {event.kind === 'retry'
            ? t('toolLog.retry', {
                attempt: event.retryAttempt ?? 1,
                delay: ((event.retryDelayMs ?? 0) / 1000).toFixed(1),
              })
            : (event.toolName ?? '')}
          {event.isError && (
            <Badge variant="destructive" className="ml-2">
              {t('toolLog.failed')}
            </Badge>
          )}
        </span>
        <span className="text-muted-foreground flex items-center gap-2 whitespace-nowrap">
          {event.durationMs !== undefined && <span>{Math.round(event.durationMs)} ms</span>}
          <time dateTime={event.atUtc}>{formatTime(event.atUtc)}</time>
        </span>
      </div>

      {event.kind !== 'retry' && (
        <>
          <p className="text-muted-foreground font-mono break-all">{event.argumentsPreview}</p>
          <p className="text-muted-foreground font-mono break-all">
            {event.kind === 'tool_started' ? (
              t('toolLog.running')
            ) : (
              <>
                {event.resultPreview}
                {event.resultBytes !== undefined && (
                  <span className="block opacity-70">
                    {t('toolLog.bytes', { bytes: formatCount(event.resultBytes) })}
                  </span>
                )}
              </>
            )}
          </p>
        </>
      )}
    </div>
  );
}

/**
 * The model's own reasoning and reply text for one turn. Reasoning is collapsed by default — it
 * is the model thinking out loud and can run long — while the reply text, the part meant to be
 * read, is shown open.
 */
function AssistantMessageRow({ event }: { event: ToolCallEvent }) {
  const t = useT();

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center justify-end">
        <time className="text-muted-foreground whitespace-nowrap" dateTime={event.atUtc}>
          {formatTime(event.atUtc)}
        </time>
      </div>

      {event.reasoningText && (
        <details className="text-muted-foreground">
          <summary className="cursor-pointer select-none">{t('toolLog.reasoning')}</summary>
          <p className="mt-1 whitespace-pre-wrap">{event.reasoningText}</p>
        </details>
      )}

      {event.responseText && <p className="whitespace-pre-wrap">{event.responseText}</p>}
    </div>
  );
}

interface LogRow {
  key: string;
  event: ToolCallEvent;
}

/**
 * One row per call: a `tool_finished` replaces the newest still-running row for the same tool in
 * the same turn, keeping the arguments that row carried. Retries get their own row, because a
 * retry is a thing that happened rather than a call that completed.
 *
 * Newest-first matching is what handles a model calling `read_file` six times in one turn: six
 * running rows, and each result closes the most recent one still open. Which of six identical
 * calls a result belongs to is not knowable from the events and does not matter — the set of rows
 * is right either way.
 */
function collapse(events: readonly ToolCallEvent[]): LogRow[] {
  const rows: LogRow[] = [];

  for (const event of events) {
    if (event.kind === 'retry') {
      rows.push({ key: `retry-${event.sequence}`, event });
      continue;
    }

    if (event.kind === 'tool_finished') {
      const open = findOpenRow(rows, event);

      if (open !== undefined) {
        const started = rows[open];
        rows[open] = {
          key: started!.key,
          event: { ...event, argumentsPreview: started!.event.argumentsPreview },
        };
        continue;
      }
    }

    rows.push({ key: `call-${event.sequence}`, event });
  }

  return rows;
}

/** The index of the newest row still showing as running for this tool and turn. */
function findOpenRow(rows: LogRow[], event: ToolCallEvent): number | undefined {
  for (let i = rows.length - 1; i >= 0; i--) {
    const row = rows[i]!;

    if (
      row.event.kind === 'tool_started' &&
      row.event.turn === event.turn &&
      row.event.toolName === event.toolName
    ) {
      return i;
    }
  }

  return undefined;
}
