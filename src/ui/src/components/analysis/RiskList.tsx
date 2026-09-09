import { TriangleAlertIcon } from 'lucide-react';
import { useT } from '@/i18n/useT';
import { cn } from '@/lib/utils';

/**
 * A list of risks, and nothing else.
 *
 * Its own file because it is the shape §0.1 promises — *"risks live in a separate column from
 * explanations"* — and it is now drawn in six places: the overview band, the risk register, and the
 * node, edge and container hover cards. One component means one answer to what a risk looks like,
 * and it means a risk can never be rendered as a paragraph inside prose by accident.
 */
export function RiskList({
  risks,
  hideWhenEmpty,
  className,
}: {
  risks: readonly string[];
  hideWhenEmpty?: boolean;
  className?: string;
}) {
  const t = useT();

  if (risks.length === 0) {
    return hideWhenEmpty ? null : (
      <p className={cn('text-sm text-muted-foreground', className)}>{t('analysis.noRisks')}</p>
    );
  }

  return (
    <ul className={cn('flex flex-col gap-1.5', className)}>
      {risks.map((risk, index) => (
        <li key={index} className="flex gap-2 text-sm">
          <TriangleAlertIcon
            className="mt-0.5 size-3.5 shrink-0 text-warning-foreground"
            aria-hidden
          />
          <span>{risk}</span>
        </li>
      ))}
    </ul>
  );
}

/**
 * The risk half of a card, as a column of its own.
 *
 * The heading and the tinted panel are the separation, made visible. A reviewer scanning for danger
 * finds it by looking at one side of the card rather than by reading four paragraphs to see whether
 * one of them turns into a warning.
 */
export function RiskColumn({
  risks,
  className,
}: {
  risks: readonly string[];
  className?: string;
}) {
  const t = useT();

  return (
    <section
      className={cn(
        'flex flex-col gap-1.5 rounded-md border border-warning/30 bg-warning/10 p-2.5',
        className,
      )}
      data-testid="risk-column"
    >
      <h4 className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-warning-foreground">
        <TriangleAlertIcon className="size-3.5" aria-hidden />
        <span>{t('analysis.risksHeading')}</span>
      </h4>

      <RiskList risks={risks} />
    </section>
  );
}
