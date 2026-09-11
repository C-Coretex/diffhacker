import * as Popover from '@radix-ui/react-popover';
import { SlidersHorizontalIcon } from 'lucide-react';
import type { AnalysisOptions } from '@/contracts';
import { useT } from '@/i18n/useT';
import { partsOff, sameOptions, verbosityLabel } from '@/lib/analysisParts';
import { cn } from '@/lib/utils';
import { Button } from '@/components/ui/button';
import { AnalysisPartsFields } from './AnalysisPartsFields';

/**
 * What the next run asks for, beside the button that spends the money.
 *
 * It starts from the defaults set in Settings, and a change here is for the next run only: the screen
 * forgets it once that run succeeds, and the host never remembers it at all. That is the difference
 * from Settings, and the popover says so, because a reviewer who trims one run should not discover a
 * week later that they trimmed every run.
 *
 * The trigger reads as a summary — the verbosity and how many parts are off — so what the next run
 * will cost is visible without opening anything, and it is marked when it differs from the defaults.
 */
export function RunOptionsPopover({
  value,
  defaults,
  onChange,
  disabled,
}: {
  value: AnalysisOptions;
  defaults: AnalysisOptions;
  onChange(next: AnalysisOptions): void;
  disabled: boolean;
}) {
  const t = useT();

  const off = partsOff(value);
  const changed = !sameOptions(value, defaults);
  const verbosity = t(verbosityLabel(value.verbosity));
  const summary =
    off === 0
      ? t('analysis.parts.triggerAll', { verbosity })
      : t('analysis.parts.triggerSome', { verbosity, count: off });

  return (
    <Popover.Root>
      <Popover.Trigger asChild>
        <Button
          size="sm"
          variant="outline"
          disabled={disabled}
          aria-label={`${t('analysis.parts.trigger')}: ${summary}`}
          title={changed ? t('analysis.parts.changed') : t('analysis.parts.trigger')}
          data-testid="run-options-button"
          data-changed={changed}
        >
          <SlidersHorizontalIcon aria-hidden />
          <span className="text-xs">{summary}</span>
          {changed && <span className="size-1.5 rounded-full bg-primary" aria-hidden />}
        </Button>
      </Popover.Trigger>

      <Popover.Portal>
        <Popover.Content
          align="end"
          sideOffset={6}
          data-testid="run-options"
          className="z-50 flex max-h-[80vh] w-[26rem] max-w-[90vw] flex-col gap-3 overflow-y-auto rounded-md border border-border bg-popover p-3 text-popover-foreground shadow-md"
        >
          <div>
            <h2 className="text-sm font-semibold">{t('analysis.parts.popoverHeading')}</h2>
            <p className="text-xs text-muted-foreground">{t('analysis.parts.popoverBody')}</p>
          </div>

          <AnalysisPartsFields value={value} onChange={onChange} testIdPrefix="run-option" />

          <div className="flex items-center justify-between gap-2 border-t border-border pt-2">
            <span className={cn('text-xs text-muted-foreground', !changed && 'invisible')}>
              {t('analysis.parts.changed')}
            </span>
            <Button
              size="sm"
              variant="ghost"
              disabled={!changed}
              onClick={() => onChange(defaults)}
              data-testid="run-options-reset"
            >
              {t('analysis.parts.reset')}
            </Button>
          </div>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
