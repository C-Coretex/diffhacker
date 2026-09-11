import { createContext, useContext, useMemo, type ReactNode } from 'react';
import type { AnalysisView } from '@/contracts';
import { useT } from '@/i18n/useT';
import { cn } from '@/lib/utils';

/** Which parts the run behind the analysis on screen was asked for. */
export interface ProducedParts {
  readonly risks: boolean;
  readonly nodeExplanations: boolean;
  readonly edgeExplanations: boolean;
  readonly containerExplanations: boolean;
}

/** Everything — what any surface outside a provider assumes, and what every analysis before 1.15 had. */
const EVERY_PART: ProducedParts = {
  risks: true,
  nodeExplanations: true,
  edgeExplanations: true,
  containerExplanations: true,
};

const Context = createContext<ProducedParts>(EVERY_PART);

/**
 * Says which parts of the analysis on screen were asked for, to everything drawn inside it.
 *
 * Provided once over the whole surface rather than read from the store by each card: the cards are
 * built per hover for three hundred boxes, and the answer is four booleans that change only when a
 * different analysis arrives. The same reasoning as `GraphActionsContext`.
 *
 * What it is for: an empty risk list on a run that was never asked for risks is not "no risks", and
 * an empty explanation on a run told to skip them is not "the model had nothing to say". The first
 * would be a false reassurance, the second a false alarm, and the reviewer is owed neither.
 */
export function AnalysisPartsProvider({ view, children }: { view: AnalysisView; children: ReactNode }) {
  const { risksProduced, nodeExplanationsProduced, edgeExplanationsProduced, containerExplanationsProduced } =
    view;

  const parts = useMemo<ProducedParts>(
    () => ({
      risks: risksProduced,
      nodeExplanations: nodeExplanationsProduced,
      edgeExplanations: edgeExplanationsProduced,
      containerExplanations: containerExplanationsProduced,
    }),
    [risksProduced, nodeExplanationsProduced, edgeExplanationsProduced, containerExplanationsProduced],
  );

  return <Context.Provider value={parts}>{children}</Context.Provider>;
}

export function useProducedParts(): ProducedParts {
  return useContext(Context);
}

/** Where an explanation would be, on a run that was not asked for it. */
export function NotRequested({ className }: { className?: string }) {
  const t = useT();

  return (
    <p className={cn('text-xs italic text-muted-foreground', className)} data-testid="explanations-not-requested">
      {t('analysis.parts.explanationsNotRequested')}
    </p>
  );
}
