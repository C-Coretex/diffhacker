import { useState } from 'react';
import { CircleHelpIcon } from 'lucide-react';
import { useT } from '@/i18n/useT';
import { useAppStore } from '@/store/appStore';
import { Button } from '@/components/ui/button';
import { dismissGuideOffer, guideOfferDismissed } from './guideOffer';

/**
 * A pointer to the step-by-step guide on the start screen, for whoever opens the application for
 * the first time and does not yet know that nothing works until a provider is added.
 *
 * Opening the guide does not dismiss it — someone who reads step 1 and comes back has not said they
 * are done with it. "Not now" does, for good.
 */
export function GuideOfferCard() {
  const t = useT();
  const openHelp = useAppStore((state) => state.openHelp);
  const setSection = useAppStore((state) => state.setHelpSection);
  const setStep = useAppStore((state) => state.setHelpGuideStep);
  const [dismissed, setDismissed] = useState(guideOfferDismissed);

  if (dismissed) return null;

  const open = () => {
    setSection('guide');
    setStep(0);
    openHelp();
  };

  const dismiss = () => {
    dismissGuideOffer();
    setDismissed(true);
  };

  return (
    <section
      aria-labelledby="guide-offer-heading"
      data-testid="guide-offer"
      className="flex flex-wrap items-center gap-4 rounded-xl border border-border bg-card px-6 py-4 shadow-sm"
    >
      <CircleHelpIcon className="size-5 shrink-0 text-primary" aria-hidden />

      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <h2 id="guide-offer-heading" className="text-sm font-semibold">
          {t('help.offer.heading')}
        </h2>
        <p className="text-sm text-muted-foreground">{t('help.offer.body')}</p>
      </div>

      <div className="flex items-center gap-2">
        <Button size="sm" onClick={open}>
          {t('help.offer.open')}
        </Button>
        <Button size="sm" variant="ghost" onClick={dismiss}>
          {t('help.offer.dismiss')}
        </Button>
      </div>
    </section>
  );
}
