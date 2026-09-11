import type { AnalysisNodeInfo, ChangedFileFactsInfo } from '@/contracts';
import { useT } from '@/i18n/useT';
import { Markdown } from '@/components/analysis/Markdown';
import { RiskColumn } from '@/components/analysis/RiskList';
import { cn } from '@/lib/utils';

/**
 * What the model wrote about one node, with its risks in a column of their own.
 *
 * Extracted from the hover card in Iteration 10 because requirement 4 wants the same four fields
 * beside the diff — "the reviewer should not return to the graph to remember why they are here". Two
 * copies of this markup would drift within a release, and the half that drifted would be the one
 * nobody was looking at.
 *
 * The risks stay a `RiskColumn` in both places for the reason Iteration 9 gave: one component means a
 * risk can never accidentally render as prose.
 */
export function NodeExplanation({
  node,
  className,
  columns = true,
}: {
  node: AnalysisNodeInfo;

  /**
   * Side by side in the hover card, which is wide and short; stacked in the diff panel, which is
   * narrow and tall and already spends its width on the code.
   */
  columns?: boolean;
  className?: string;
}) {
  const t = useT();

  return (
    <div
      className={cn(
        'gap-3',
        columns ? 'grid grid-cols-[1.6fr_1fr]' : 'flex flex-col',
        className,
      )}
      data-testid="node-explanation"
    >
      <div className="flex min-w-0 flex-col gap-2.5">
        <Prose label={t('analysis.nodeWhatChanged')} value={node.whatChanged} />
        <Prose label={t('analysis.nodeWhyItChanged')} value={node.whyItChanged} />
        <Prose label={t('analysis.nodeAffects')} value={node.howItAffectsOthers} />
        <Prose label={t('analysis.nodeNotes')} value={node.implementationNotes} />
      </div>

      <RiskColumn risks={node.risks} className="min-w-0 self-start" />
    </div>
  );
}

/** One labelled prose field. Absent rather than blank when the model had nothing to say. */
export function Prose({ label, value }: { label: string; value: string }) {
  if (!value) return null;

  return (
    <div>
      <h4 className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
        {label}
      </h4>
      <Markdown text={value} className="text-sm leading-relaxed" />
    </div>
  );
}

/**
 * The line counts and status git recorded when the run happened.
 *
 * Absent rather than zero, all the way from git: a binary or a submodule pointer has no countable
 * line change, and "+0 −0" would claim it was touched and nothing happened.
 */
export function ChangeStats({ facts }: { facts: ChangedFileFactsInfo | undefined }) {
  const t = useT();

  if (!facts) return <span>{t('analysis.graph.noCounts')}</span>;

  const counts = facts.isBinary
    ? t('analysis.graph.binary')
    : facts.linesAdded === undefined && facts.linesRemoved === undefined
      ? t('analysis.graph.noCounts')
      : `+${facts.linesAdded ?? 0} −${facts.linesRemoved ?? 0}`;

  return (
    <span className="tabular-nums">
      {counts}
      {' · '}
      {facts.status}
      {facts.language && ` · ${facts.language}`}
      {facts.project && ` · ${facts.project}`}
    </span>
  );
}
