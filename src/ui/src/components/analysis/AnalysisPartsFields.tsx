import type { AnalysisOptions } from '@/contracts';
import { useT } from '@/i18n/useT';
import {
  CONTENT_PARTS,
  GROUPING_PARTS,
  VERBOSITY_LEVELS,
  type AnalysisPartInfo,
} from '@/lib/analysisParts';
import { cn } from '@/lib/utils';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';

/**
 * The controls for which parts of an analysis a run asks for, and how much it writes.
 *
 * One component for both places they appear — the defaults in Settings and the one-off options
 * beside the run button — so the two can never offer different choices or describe one differently.
 * It holds no state: each place owns its value and decides what a change means (saved, or used once).
 *
 * Every checkbox carries its explanation as visible text rather than a tooltip. This is the decision
 * about what a run costs and what it will leave out, and "what do I lose?" should not be a hover away.
 */
export function AnalysisPartsFields({
  value,
  onChange,
  disabled,
  testIdPrefix,
}: {
  value: AnalysisOptions;
  onChange(next: AnalysisOptions): void;
  disabled?: boolean;

  /** Distinguishes the two copies' test and element ids, which can be on screen in one session. */
  testIdPrefix: string;
}) {
  const t = useT();
  const active = VERBOSITY_LEVELS.find((level) => level.value === value.verbosity) ?? VERBOSITY_LEVELS[0]!;

  return (
    <div className="flex flex-col gap-4">
      <PartGroup
        legend={t('analysis.parts.groupingsLegend')}
        parts={GROUPING_PARTS}
        value={value}
        onChange={onChange}
        disabled={disabled}
        testIdPrefix={testIdPrefix}
      />

      <PartGroup
        legend={t('analysis.parts.contentLegend')}
        parts={CONTENT_PARTS}
        value={value}
        onChange={onChange}
        disabled={disabled}
        testIdPrefix={testIdPrefix}
      />

      <fieldset className="flex flex-col gap-1.5">
        <legend className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          {t('analysis.parts.verbosity')}
        </legend>

        <div
          role="group"
          aria-label={t('analysis.parts.verbosity')}
          className="flex w-fit items-center gap-0.5 rounded-md border border-border p-0.5"
        >
          {VERBOSITY_LEVELS.map((level) => {
            const isActive = level.value === value.verbosity;

            return (
              <button
                key={level.value}
                type="button"
                disabled={disabled}
                aria-pressed={isActive}
                title={t(level.body)}
                onClick={() => onChange({ ...value, verbosity: level.value })}
                data-testid={`${testIdPrefix}-verbosity-${level.value}`}
                className={cn(
                  'rounded px-2.5 py-1 text-xs text-muted-foreground',
                  'hover:bg-accent hover:text-foreground disabled:opacity-50',
                  isActive && 'bg-accent text-foreground',
                )}
              >
                {t(level.label)}
              </button>
            );
          })}
        </div>

        <p className="text-xs text-muted-foreground">{t(active.body)}</p>
      </fieldset>
    </div>
  );
}

function PartGroup({
  legend,
  parts,
  value,
  onChange,
  disabled,
  testIdPrefix,
}: {
  legend: string;
  parts: readonly AnalysisPartInfo[];
  value: AnalysisOptions;
  onChange(next: AnalysisOptions): void;
  disabled?: boolean;
  testIdPrefix: string;
}) {
  const t = useT();

  return (
    <fieldset className="flex flex-col gap-2.5">
      <legend className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
        {legend}
      </legend>

      {parts.map(({ part, label, body, slug }) => {
        const id = `${testIdPrefix}-${slug}`;

        return (
          <div key={part} className="flex items-start gap-2">
            <Checkbox
              id={id}
              checked={value[part]}
              disabled={disabled}
              onChange={(event) => onChange({ ...value, [part]: event.target.checked })}
              aria-describedby={`${id}-body`}
              className="mt-0.5"
              data-testid={id}
            />
            <div className="flex min-w-0 flex-col gap-0.5">
              <Label htmlFor={id} className="text-sm font-medium">
                {t(label)}
              </Label>
              <p id={`${id}-body`} className="text-xs text-muted-foreground">
                {t(body)}
              </p>
            </div>
          </div>
        );
      })}
    </fieldset>
  );
}
