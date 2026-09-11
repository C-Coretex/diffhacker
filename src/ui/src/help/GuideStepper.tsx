import type { KeyboardEvent } from 'react';
import { ArrowLeftIcon, ArrowRightIcon, CheckIcon, ImageOffIcon } from 'lucide-react';
import { useT } from '@/i18n/useT';
import { cn } from '@/lib/utils';
import { useAppStore } from '@/store/appStore';
import { Button } from '@/components/ui/button';
import { Markdown } from '@/components/analysis/Markdown';
import { GUIDE_STEPS, type GuideStepId } from './guideSteps';
import { screenshotUrl } from './guideImages';
import { stepKey } from './helpContent';

/**
 * The step-by-step guide, one step at a time.
 *
 * ```
 * ┌───────────────┬──────────────────────────────────────────────┐
 * │ 1 Add a prov… │ Step 3 of 15                  [‹ Back][Next ›]│
 * │ 2 Test the …  │ Choose what an analysis asks for             │
 * │▸3 Choose wh…  │ instructions                                 │
 * │ 4 Open a re…  │ ┌──────────────────────────────────────────┐ │
 * │ …             │ │               screenshot                 │ │
 * └───────────────┴──────────────────────────────────────────────┘
 * ```
 *
 * Paged rather than one long scroll, because each step is a thing to go and do: the reader leaves
 * Help to do it and comes back for the next one, and the step they were on is still selected (it is
 * store state, not local state, for exactly that reason). The list beside it is the way to skip —
 * someone who already has a provider starts at step 4.
 *
 * The instructions come before the picture. They are short, and a screenshot is easier to read once
 * you know what it is showing you; `docs/user-guide.md` is generated in the same order.
 */
export function GuideStepper() {
  const t = useT();
  const stored = useAppStore((state) => state.helpGuideStep);
  const setStep = useAppStore((state) => state.setHelpGuideStep);
  const closeHelp = useAppStore((state) => state.closeHelp);

  const total = GUIDE_STEPS.length;
  const index = clamp(stored, total);
  const step = GUIDE_STEPS[index]!;
  const last = index === total - 1;

  const go = (next: number) => setStep(clamp(next, total));

  // Arrow keys page, from anywhere inside the guide — the Next button keeps focus after a click, so
  // "click once, then arrow through" works without the reader having to find the stepper first.
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.metaKey || event.ctrlKey || event.altKey) return;

    if (event.key === 'ArrowRight' && !last) {
      event.preventDefault();
      go(index + 1);
    } else if (event.key === 'ArrowLeft' && index > 0) {
      event.preventDefault();
      go(index - 1);
    }
  };

  return (
    <div
      className="grid gap-6 md:grid-cols-[15rem_minmax(0,1fr)]"
      onKeyDown={onKeyDown}
      data-testid="guide"
      data-step={step.id}
    >
      <nav aria-label={t('help.guide.stepsLabel')} className="flex flex-col gap-3">
        <p className="text-sm text-muted-foreground">{t('help.guide.intro')}</p>

        <ol className="flex flex-col gap-0.5">
          {GUIDE_STEPS.map((candidate, position) => (
            <li key={candidate.id}>
              <button
                type="button"
                onClick={() => go(position)}
                aria-current={position === index ? 'step' : undefined}
                data-testid={`guide-step-${candidate.id}`}
                className={cn(
                  'flex w-full items-baseline gap-2 rounded-md px-2 py-1.5 text-left text-sm',
                  'text-muted-foreground hover:bg-accent hover:text-foreground',
                  position === index && 'bg-accent font-medium text-foreground',
                )}
              >
                <span className="w-5 shrink-0 text-right text-xs tabular-nums">{position + 1}</span>
                <span className="min-w-0">{t(stepKey(candidate.id, 'title'))}</span>
              </button>
            </li>
          ))}
        </ol>
      </nav>

      <article aria-labelledby="guide-step-title" className="flex min-w-0 flex-col gap-4">
        <header className="flex flex-wrap items-end justify-between gap-3">
          <div className="flex flex-col gap-1">
            <p className="text-xs text-muted-foreground" data-testid="guide-position">
              {t('help.guide.position', { current: index + 1, total })}
            </p>
            <h3 id="guide-step-title" className="text-lg font-semibold tracking-tight">
              {t(stepKey(step.id, 'title'))}
            </h3>
          </div>

          <StepButtons
            index={index}
            last={last}
            onBack={() => go(index - 1)}
            onNext={() => go(index + 1)}
            onFinish={closeHelp}
          />
        </header>

        <Markdown
          text={t(stepKey(step.id, 'body'))}
          className="max-w-3xl gap-2.5 text-sm leading-relaxed"
        />

        <Screenshot id={step.id} />

        <footer className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
          <p className="text-xs text-muted-foreground">{t('help.guide.keyboardHint')}</p>
          <StepButtons
            index={index}
            last={last}
            onBack={() => go(index - 1)}
            onNext={() => go(index + 1)}
            onFinish={closeHelp}
          />
        </footer>
      </article>
    </div>
  );
}

/**
 * Back and Next, drawn twice — above the instructions and under the screenshot — because on a
 * laptop the screenshot pushes the bottom pair off the screen, and scrolling back up to move on is
 * the chore the paging was meant to remove.
 */
function StepButtons({
  index,
  last,
  onBack,
  onNext,
  onFinish,
}: {
  index: number;
  last: boolean;
  onBack: () => void;
  onNext: () => void;
  onFinish: () => void;
}) {
  const t = useT();

  return (
    <div className="flex items-center gap-2">
      <Button variant="outline" size="sm" onClick={onBack} disabled={index === 0}>
        <ArrowLeftIcon aria-hidden />
        {t('help.guide.previous')}
      </Button>

      {last ? (
        <Button size="sm" onClick={onFinish}>
          <CheckIcon aria-hidden />
          {t('help.guide.finish')}
        </Button>
      ) : (
        <Button size="sm" onClick={onNext}>
          {t('help.guide.next')}
          <ArrowRightIcon aria-hidden />
        </Button>
      )}
    </div>
  );
}

/**
 * The step's screenshot, at no more than its own size.
 *
 * `max-w-full` rather than `w-full`: some steps show the whole window and some one control, and a
 * crop of a popover blown up to the width of the page would be a blurry popover. The images are
 * captured at one pixel per CSS pixel, so natural size is the size it looked on screen.
 */
function Screenshot({ id }: { id: GuideStepId }) {
  const t = useT();
  const url = screenshotUrl(id);
  const alt = t(stepKey(id, 'alt'));

  if (!url) {
    return (
      <div
        role="img"
        aria-label={alt}
        data-testid="guide-screenshot-missing"
        className="flex min-h-48 flex-col items-center justify-center gap-2 rounded-lg border border-dashed border-border p-6 text-center text-sm text-muted-foreground"
      >
        <ImageOffIcon className="size-5" aria-hidden />
        {t('help.guide.missingScreenshot')}
      </div>
    );
  }

  return (
    <figure className="m-0">
      <img
        src={url}
        alt={alt}
        data-testid="guide-screenshot"
        className="h-auto max-w-full rounded-lg border border-border shadow-sm"
      />
    </figure>
  );
}

function clamp(step: number, total: number): number {
  return Math.min(Math.max(Math.trunc(step), 0), total - 1);
}
