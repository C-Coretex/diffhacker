/**
 * Whether the start screen still offers the step-by-step guide.
 *
 * Remembered in `localStorage`, for the reasons `theme/useTheme.ts` gives for the colour scheme: it is
 * presentation and nothing else, and a schema, a method and a table for one boolean would be all
 * cost. Once dismissed it stays dismissed — the Help button in the header is always there, and a
 * card that came back on every launch would be the nag it was meant not to be.
 */
const STORAGE_KEY = 'diffhacker.help.guideOfferDismissed';

export function guideOfferDismissed(): boolean {
  try {
    return window.localStorage?.getItem(STORAGE_KEY) === 'true';
  } catch {
    // Storage blocked or throwing on access. Offering the guide is the harmless answer.
    return false;
  }
}

export function dismissGuideOffer(): void {
  try {
    window.localStorage?.setItem(STORAGE_KEY, 'true');
  } catch {
    // The card still goes for this session; it just comes back after a restart.
  }
}
