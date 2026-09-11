import { Fragment, useEffect, useMemo, useState, type ReactNode } from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';
import type { AnalysisToolCallInfo, AnalysisView } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { formatBytes, formatCount } from '@/i18n/format';
import { useT } from '@/i18n/useT';
import { getAnalysisTrace } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Badge } from '@/components/ui/badge';

/**
 * Previews the host keeps, in characters. Mirrors `LlmToolCallRecord.PreviewLength` and
 * `ResultPreviewLength`, so the sentence saying how much of each call is shown tells the truth.
 */
const ARGUMENTS_PREVIEW = 200;
const RESULT_PREVIEW = 500;

/**
 * Requirement 7: the whole ordered trace of what the model asked for, and how large each answer
 * was.
 *
 * Folded until asked for, and fetched only then. A run may make five hundred calls, and the view
 * this band is drawn from travels on every read and every grouping switch; the trace does not
 * belong on it, and a reviewer who never opens this never pays for it.
 *
 * Ordered by the model's own order — ordinal, oldest first — rather than newest first like the
 * live log. The live log answers "what is it doing now"; this answers "what did it do", and that is
 * a story read from the beginning.
 */
export function ToolCallInspector({ view }: { view: AnalysisView }) {
  const t = useT();
  const [open, setOpen] = useState(false);

  return (
    <section data-testid="tool-call-inspector">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        aria-expanded={open}
        className="flex w-full items-center gap-1.5 text-left text-sm font-semibold hover:text-primary"
      >
        {open ? <ChevronDown className="size-4" aria-hidden /> : <ChevronRight className="size-4" aria-hidden />}
        {t('analysis.inspector.toggle', { count: formatCount(view.toolCallCount) })}
      </button>

      {!open && (
        <p className="mt-1 pl-5 text-xs text-muted-foreground">
          {t('analysis.inspector.body', { arguments: ARGUMENTS_PREVIEW, result: RESULT_PREVIEW })}
        </p>
      )}

      {open && <Trace view={view} />}
    </section>
  );
}

function Trace({ view }: { view: AnalysisView }) {
  const t = useT();
  const client = useRpc();
  const trace = useAppStore((state) => state.analysisTrace);
  const setTrace = useAppStore((state) => state.setAnalysisTrace);
  const [error, setError] = useState<string>();
  const [tool, setTool] = useState<string>();

  const analysisId = view.analysisId;

  // Only a trace of the analysis on screen counts. The store drops one that arrives for another,
  // but a trace fetched before a switch is still sitting there until the next one lands.
  const current = trace?.analysisId === analysisId ? trace : undefined;
  const loaded = current !== undefined;

  useEffect(() => {
    if (!client || !analysisId || loaded) return;

    setError(undefined);

    getAnalysisTrace(client, { repositoryPath: view.repositoryPath, analysisId })
      .then(setTrace)
      .catch((caught: unknown) => setError(describeError(caught)));
  }, [client, analysisId, loaded, view.repositoryPath, setTrace]);

  const calls = useMemo(() => current?.toolCalls ?? [], [current]);
  const byTool = useMemo(() => totalsByTool(calls), [calls]);

  if (error) {
    return (
      <p role="alert" className="mt-3 pl-5 text-sm text-destructive">
        {t('analysis.inspector.loadFailed')} {error}
      </p>
    );
  }

  if (!current) {
    return <p className="mt-3 pl-5 text-sm text-muted-foreground">{t('analysis.inspector.loading')}</p>;
  }

  const shown = tool === undefined ? calls : calls.filter((call) => call.toolName === tool);
  const totalBytes = calls.reduce((sum, call) => sum + call.resultBytes, 0);

  return (
    <div className="mt-3 flex flex-col gap-4 pl-5">
      <p className="text-xs text-muted-foreground">
        {t('analysis.inspector.body', { arguments: ARGUMENTS_PREVIEW, result: RESULT_PREVIEW })}
      </p>

      {calls.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('analysis.inspector.empty')}</p>
      ) : (
        <>
          <p className="text-sm" data-testid="tool-call-totals">
            {t('analysis.inspector.totals', {
              count: formatCount(calls.length),
              size: formatBytes(totalBytes),
            })}
          </p>

          {/*
            A filter rather than a second table. What a reviewer usually wants to know is "what did
            it read", and the answer is one tool's rows in order, not a pivot.
          */}
          <div role="group" aria-label={t('analysis.inspector.byTool')} className="flex flex-wrap gap-2">
            <FilterChip pressed={tool === undefined} onClick={() => setTool(undefined)}>
              {t('analysis.inspector.all', { count: formatCount(calls.length) })}
            </FilterChip>

            {byTool.map((entry) => (
              <FilterChip
                key={entry.tool}
                pressed={tool === entry.tool}
                onClick={() => setTool(tool === entry.tool ? undefined : entry.tool)}
              >
                {t('analysis.inspector.toolChip', {
                  tool: entry.tool,
                  count: formatCount(entry.count),
                  size: formatBytes(entry.bytes),
                })}
              </FilterChip>
            ))}
          </div>

          <div className="max-h-96 overflow-auto rounded-md border">
            <table className="w-full text-left text-xs">
              <thead className="sticky top-0 bg-card text-muted-foreground">
                <tr className="border-b">
                  <th scope="col" className="px-2 py-1.5 text-right font-medium">
                    {t('analysis.inspector.ordinal')}
                  </th>
                  <th scope="col" className="px-2 py-1.5 text-right font-medium">
                    {t('analysis.inspector.turn')}
                  </th>
                  <th scope="col" className="px-2 py-1.5 font-medium">
                    {t('analysis.inspector.tool')}
                  </th>
                  <th scope="col" className="px-2 py-1.5 font-medium">
                    {t('analysis.inspector.arguments')}
                  </th>
                  <th scope="col" className="px-2 py-1.5 text-right font-medium">
                    {t('analysis.inspector.size')}
                  </th>
                  <th scope="col" className="px-2 py-1.5 text-right font-medium">
                    {t('analysis.inspector.duration')}
                  </th>
                </tr>
              </thead>
              <tbody>
                {shown.map((call) => (
                  <CallRow key={call.ordinal} call={call} />
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      <section className="flex flex-col gap-2">
        <h4 className="text-xs font-semibold">{t('analysis.inspector.progressHeading')}</h4>

        {current.progressMessages.length === 0 ? (
          <p className="text-xs text-muted-foreground">{t('analysis.inspector.progressEmpty')}</p>
        ) : (
          // The model's own words, as written: run data, not copy.
          <ol className="list-decimal pl-5 text-xs text-muted-foreground" data-testid="trace-progress">
            {current.progressMessages.map((message, index) => (
              <li key={index}>{message}</li>
            ))}
          </ol>
        )}
      </section>
    </div>
  );
}

/**
 * One call, and — on request — what it returned. The result preview is folded because five hundred
 * of them open at once would bury the order the table exists to show.
 */
function CallRow({ call }: { call: AnalysisToolCallInfo }) {
  const t = useT();
  const [open, setOpen] = useState(false);

  return (
    <Fragment>
      <tr data-testid="tool-call-row" data-ordinal={call.ordinal} className="border-b align-top last:border-b-0">
        <td className="px-2 py-1.5 text-right tabular-nums text-muted-foreground">{call.ordinal}</td>
        <td className="px-2 py-1.5 text-right tabular-nums text-muted-foreground">{call.turn}</td>
        <td className="px-2 py-1.5 font-mono whitespace-nowrap">
          <button
            type="button"
            onClick={() => setOpen(!open)}
            aria-expanded={open}
            aria-label={t('analysis.inspector.showResult', { tool: call.toolName })}
            className="flex items-center gap-1 hover:text-primary"
          >
            {open ? <ChevronDown className="size-3" aria-hidden /> : <ChevronRight className="size-3" aria-hidden />}
            <span data-testid="tool-call-name">{call.toolName}</span>
          </button>
          {call.isError && (
            <Badge variant="destructive" className="mt-1">
              {t('analysis.inspector.failed')}
            </Badge>
          )}
        </td>
        <td className="max-w-md px-2 py-1.5 font-mono break-all text-muted-foreground" title={call.argumentsPreview}>
          {call.argumentsPreview}
        </td>
        <td
          className="px-2 py-1.5 text-right tabular-nums whitespace-nowrap"
          data-testid="tool-call-size"
          title={t('toolLog.bytes', { bytes: formatCount(call.resultBytes) })}
        >
          {formatBytes(call.resultBytes)}
        </td>
        <td className="px-2 py-1.5 text-right tabular-nums whitespace-nowrap text-muted-foreground">
          {Math.round(call.durationMs)} ms
        </td>
      </tr>

      {open && (
        <tr className="border-b bg-muted/40">
          <td colSpan={6} className="px-3 py-2">
            <pre className="font-mono text-xs whitespace-pre-wrap break-all text-muted-foreground">
              {call.resultPreview}
            </pre>
          </td>
        </tr>
      )}
    </Fragment>
  );
}

function FilterChip({
  pressed,
  onClick,
  children,
}: {
  pressed: boolean;
  onClick(): void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-pressed={pressed}
      onClick={onClick}
      className={
        pressed
          ? 'rounded-full border border-primary bg-primary/10 px-2.5 py-0.5 font-mono text-xs text-primary'
          : 'rounded-full border border-border px-2.5 py-0.5 font-mono text-xs text-muted-foreground hover:bg-accent'
      }
    >
      {children}
    </button>
  );
}

interface ToolTotal {
  tool: string;
  count: number;
  bytes: number;
}

/** Calls and bytes per tool, busiest first — where the run's reading actually went. */
export function totalsByTool(calls: readonly AnalysisToolCallInfo[]): ToolTotal[] {
  const totals = new Map<string, ToolTotal>();

  for (const call of calls) {
    const entry = totals.get(call.toolName) ?? { tool: call.toolName, count: 0, bytes: 0 };
    entry.count++;
    entry.bytes += call.resultBytes;
    totals.set(call.toolName, entry);
  }

  return [...totals.values()].sort((a, b) => b.count - a.count || a.tool.localeCompare(b.tool));
}
