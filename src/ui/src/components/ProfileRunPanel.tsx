import { Loader2Icon } from 'lucide-react';
import type { AnalysisProgress, AnalysisProgressPhase, ToolCallEvent } from '@/contracts';
import { formatCount } from '@/i18n/format';
import { useT } from '@/i18n/useT';
import { useAppStore } from '@/store/appStore';
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
export function RunPanel({ progress, events, latest, onCancel }: RunPanelProps) {
  const t = useT();

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
          {progress?.phase && (
            <p className="text-muted-foreground text-xs">{t(phaseLabels[progress.phase])}</p>
          )}
        </div>

        {latest && (
          <div className="text-muted-foreground flex flex-wrap items-center gap-3 text-xs">
            <span>{t('toolLog.turn', { turn: latest.turn })}</span>
            <span>
              {t('toolLog.tokens', {
                input: formatCount(latest.inputTokens),
                output: formatCount(latest.outputTokens),
              })}
            </span>
            <span>
              {latest.costUsd === undefined
                ? t('toolLog.costUnknown')
                : t('toolLog.cost', { cost: latest.costUsd.toFixed(4) })}
            </span>
          </div>
        )}

        <ToolLog events={events} />
      </CardContent>
    </Card>
  );
}

/** The profile run's slice of the store, in the shared panel. */
export function ProfileRunPanel({ onCancel }: { onCancel(): void }) {
  return (
    <RunPanel
      progress={useAppStore((state) => state.profileRunProgress)}
      events={useAppStore((state) => state.profileRunEvents)}
      latest={useAppStore((state) => state.profileRunLatest)}
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
      onCancel={onCancel}
    />
  );
}

/**
 * The tool log itself. Also used after a run, from the stored trace, which is why it takes its
 * events as a prop rather than reading the store.
 */
export function ToolLog({ events }: { events: readonly ToolCallEvent[] }) {
  const t = useT();

  if (events.length === 0) {
    return <p className="text-muted-foreground text-sm">{t('toolLog.empty')}</p>;
  }

  // Started and finished arrive as separate events; the table shows one row per call, so a
  // finished event replaces the started one it matches on tool name within the same turn.
  const rows = collapse(events);

  return (
    <div className="max-h-80 overflow-auto rounded-md border">
      <table className="w-full text-left text-xs">
        <thead className="bg-muted/50 sticky top-0">
          <tr>
            <th scope="col" className="px-3 py-2 font-medium">
              {t('toolLog.columnTool')}
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              {t('toolLog.columnArguments')}
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              {t('toolLog.columnResult')}
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              {t('toolLog.columnDuration')}
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} className="border-t align-top">
              <td className="px-3 py-2 font-mono whitespace-nowrap">
                {row.event.kind === 'retry'
                  ? t('toolLog.retry', {
                      attempt: row.event.retryAttempt ?? 1,
                      delay: ((row.event.retryDelayMs ?? 0) / 1000).toFixed(1),
                    })
                  : (row.event.toolName ?? '')}
                {row.event.isError && (
                  <Badge variant="destructive" className="ml-2">
                    {t('toolLog.failed')}
                  </Badge>
                )}
              </td>
              <td className="text-muted-foreground max-w-64 px-3 py-2 font-mono break-all">
                {row.event.argumentsPreview}
              </td>
              <td className="text-muted-foreground max-w-96 px-3 py-2 font-mono break-all">
                {row.event.kind === 'tool_started' ? (
                  t('toolLog.running')
                ) : (
                  <>
                    {row.event.resultPreview}
                    {row.event.resultBytes !== undefined && (
                      <span className="block opacity-70">
                        {t('toolLog.bytes', { bytes: formatCount(row.event.resultBytes) })}
                      </span>
                    )}
                  </>
                )}
              </td>
              <td className="text-muted-foreground px-3 py-2 text-right whitespace-nowrap">
                {row.event.durationMs === undefined ? '' : `${Math.round(row.event.durationMs)} ms`}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
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
