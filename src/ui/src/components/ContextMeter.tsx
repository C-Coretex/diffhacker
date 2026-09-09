import * as Popover from '@radix-ui/react-popover';
import type { ToolCallEvent } from '@/contracts';
import { formatCount } from '@/i18n/format';
import type { ResourceKey } from '@/i18n/translate';
import { useT } from '@/i18n/useT';

/**
 * How full the model's context is, live (requirement 14), against the window it has (requirement 16).
 *
 * ```
 * known window:    Context  84,312 / 200,000  (42%)  [▓▓▓▓▓░░░░░░░]
 * unknown window:  Context  84,312 tokens · window unknown
 * ```
 *
 * **Two units, deliberately, and the panel says which is which.** The total is the provider's own
 * count of the last request, in tokens, because that is the only exact answer available. The
 * breakdown is in characters, because the application built every one of those strings and can
 * count them exactly, whereas per-category tokens would mean shipping a tokeniser per provider and
 * still being wrong on the next model. Presenting an estimate beside a real number, in the same
 * unit, would make both look equally trustworthy.
 *
 * **It is not a budget.** Nothing here stops a run. The number is shown so a reviewer can see a
 * long exploration filling up, and so "why did it start forgetting things" has a visible answer —
 * the pruned row.
 */
export function ContextMeter({ event }: { event?: ToolCallEvent }) {
  const t = useT();

  if (!event || event.contextInstructionsCharacters === undefined) return null;

  const used = event.contextTokens;
  const window = event.contextWindowTokens;
  const percent = used !== undefined && window ? Math.min(100, (used / window) * 100) : null;

  return (
    <Popover.Root>
      <Popover.Trigger asChild>
        <button
          type="button"
          className="flex items-center gap-2 rounded px-1.5 py-0.5 text-xs text-muted-foreground hover:bg-accent"
          aria-label={t('toolLog.contextLabel')}
        >
          <span>{t('toolLog.context')}</span>

          {used === undefined ? (
            // A provider that reports no usage leaves this genuinely unknown. Saying so beats a
            // zero, which would read as an empty context.
            <span>{t('toolLog.contextUnreported')}</span>
          ) : window ? (
            <>
              <span className="tabular-nums">
                {t('toolLog.contextOf', {
                  used: formatCount(used),
                  window: formatCount(window),
                  percent: Math.round(percent ?? 0),
                })}
              </span>
              <span
                aria-hidden
                className="h-1.5 w-24 overflow-hidden rounded-full bg-muted"
              >
                <span
                  className="block h-full rounded-full bg-primary"
                  style={{ width: `${percent ?? 0}%` }}
                />
              </span>
            </>
          ) : (
            // No bar without a denominator. A guessed window would make a meter that is
            // confidently wrong, which is worse than one that admits it does not know.
            <span className="tabular-nums">
              {t('toolLog.contextUnknownWindow', { used: formatCount(used) })}
            </span>
          )}
        </button>
      </Popover.Trigger>

      <Popover.Portal>
        <Popover.Content
          align="start"
          sideOffset={6}
          className="z-50 w-80 rounded-md border border-border bg-popover p-3 text-popover-foreground shadow-md"
        >
          <Breakdown event={event} />
          <Popover.Arrow className="fill-popover" />
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}

/** What is filling it, by category. */
function Breakdown({ event }: { event: ToolCallEvent }) {
  const t = useT();

  const rows: { key: ResourceKey; value: number | undefined }[] = [
    { key: 'toolLog.contextInstructions', value: event.contextInstructionsCharacters },
    { key: 'toolLog.contextSchema', value: event.contextSchemaCharacters },
    { key: 'toolLog.contextTools', value: event.contextToolDefinitionCharacters },
    { key: 'toolLog.contextOpening', value: event.contextOpeningCharacters },
    { key: 'toolLog.contextToolResults', value: event.contextToolResultCharacters },
    { key: 'toolLog.contextAssistant', value: event.contextAssistantCharacters },
    { key: 'toolLog.contextReasoning', value: event.contextReasoningCharacters },
  ];

  const total = rows.reduce((sum, row) => sum + (row.value ?? 0), 0);

  return (
    <div className="flex flex-col gap-2 text-xs">
      <h3 className="font-semibold">{t('toolLog.contextBreakdown')}</h3>
      <p className="text-muted-foreground">{t('toolLog.contextUnits')}</p>

      <dl className="flex flex-col gap-1">
        {rows.map((row) => (
          <div key={row.key} className="flex items-baseline gap-2">
            <dt className="flex-1 text-muted-foreground">{t(row.key)}</dt>

            {row.value === undefined ? (
              // Reasoning is the one that can be genuinely absent: most providers bill for it and
              // never show it, and reporting zero would claim the model did none.
              <dd className="text-muted-foreground">{t('toolLog.contextNotReported')}</dd>
            ) : (
              <>
                <dd className="tabular-nums">{formatCount(row.value)}</dd>
                <dd className="w-10 text-right tabular-nums text-muted-foreground">
                  {total === 0 ? '—' : `${Math.round((row.value / total) * 100)}%`}
                </dd>
              </>
            )}
          </div>
        ))}
      </dl>

      {event.contextPrunedCharacters !== undefined && event.contextPrunedCharacters > 0 && (
        <p className="border-t border-border pt-2 text-muted-foreground">
          {t('toolLog.contextPruned', {
            pruned: formatCount(event.contextPrunedCharacters),
          })}
        </p>
      )}
    </div>
  );
}
