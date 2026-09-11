import type { AnalysisNodeInfoState } from '@/contracts';
import { translate as t } from '@/i18n/translate';

/**
 * The pieces a node is drawn from, shared by the box that stands for one node (`FileNode`) and the
 * box that stands for an abstraction and its implementations (`ImplementationGroupNode`), whose rows
 * are the same node drawn shorter. One definition, so "dashed means added" cannot come to mean two
 * things in two places.
 */

/**
 * Line counts, or an honest silence.
 *
 * Absent rather than zero all the way from git: a binary or a submodule pointer has no countable
 * line change, and "+0 −0" would claim it was touched and nothing happened.
 */
export function Stats({
  added,
  removed,
  binary,
}: {
  added?: number;
  removed?: number;
  binary?: boolean;
}) {
  if (binary) return <span>{t('analysis.graph.binary')}</span>;
  if (added === undefined && removed === undefined) return <span>{t('analysis.graph.noCounts')}</span>;

  return (
    <span className="tabular-nums">
      {added !== undefined && <span className="text-emerald-700 dark:text-emerald-400">+{added}</span>}
      {added !== undefined && removed !== undefined && ' '}
      {removed !== undefined && <span className="text-rose-700 dark:text-rose-400">−{removed}</span>}
    </span>
  );
}

/**
 * How loudly a box speaks, from the model's importance rank (requirement 6).
 *
 * The floor is the point. §0.2.5 says every changed file appears in the graph, so "de-emphasised"
 * can only mean *quieter*, never smaller, never hidden, never harder to find — the box keeps its
 * size, its border style, its badges and its project colour, all of which §0.6 already spent on
 * something else. What is left is opacity and weight, and 70 % is where the fade stops: enough that
 * a screen of thirty boxes visibly sorts itself, little enough that a faded box is still readable
 * without doing anything.
 *
 * `lifted` — a search match or a jump — and `group-hover` both restore full strength, so looking at
 * a trivial node is never a worse experience than looking at an important one.
 */
export function emphasisFor(
  importance: number,
  lifted: boolean,
): { level: string; box: string; rail: string; fileName: string; title: string } {
  if (!lifted && importance <= 2) {
    return {
      level: 'low',
      box: 'opacity-70 transition-opacity hover:opacity-100',
      rail: 'w-1',
      fileName: 'font-medium',
      title: 'text-muted-foreground',
    };
  }

  if (importance >= 4) {
    return { level: 'high', box: '', rail: 'w-1.5', fileName: 'font-bold', title: 'font-medium' };
  }

  return { level: 'normal', box: '', rail: 'w-1', fileName: 'font-semibold', title: '' };
}

/**
 * The border says what happened to the file. Distinguishable at low zoom, where a badge is a
 * smudge: solid for changed, dashed for added, dotted for deleted, double for a file that did not
 * change but matters anyway, and a destructive colour for risky over whichever of those applies.
 */
export function borderFor(states: ReadonlySet<AnalysisNodeInfoState>): string {
  const shape = states.has('added')
    ? 'border-dashed'
    : states.has('deleted')
      ? 'border-dotted'
      : states.has('unchanged_relevant')
        ? 'border-double border-4'
        : 'border-solid';

  return states.has('risky') ? `${shape} !border-destructive` : shape;
}

/**
 * The git status word, falling back to the model's state when the changeset facts are missing —
 * which is what an analysis stored before schema 1.8 looks like.
 */
export function statusLabel(
  status: string | undefined,
  states: ReadonlySet<AnalysisNodeInfoState>,
): string {
  if (status) return status;
  if (states.has('added')) return t('analysis.state.added');
  if (states.has('deleted')) return t('analysis.state.deleted');
  if (states.has('unchanged_relevant')) return t('analysis.state.unchanged_relevant');
  return t('analysis.state.changed');
}
