import type { MouseEvent, ReactNode } from 'react';
import { CircleCheckBigIcon, CircleIcon, ExternalLinkIcon, FileDiffIcon } from 'lucide-react';
import type { AnalysisNodeInfo, ChangedFileFactsInfo } from '@/contracts';
import { translate as t } from '@/i18n/translate';
import { cn } from '@/lib/utils';
import { editorChoices } from '@/components/diff/useEditors';
import { useGraphActions } from './graphActions';

/**
 * The actions that belong to one file, on the file.
 *
 * Iteration 10 put "open the diff" on the hover card because Iteration 9 had spent the single click
 * on pinning that card. The card is still the place the *explanation* lives, but a reviewer who
 * already knows which file they want should not have to summon a card to reach a button — so the
 * three things they do to a file are on the box itself: read the diff, hand it to an external editor,
 * mark it read.
 *
 * ```
 * ┌──────────────────────────────────────────┐
 * │▏ ▲  Cache.cs                        ⚑ ⑤ │
 * │▏ src/…/Storage/                          │
 * │▏ Evicts on tenant change                 │
 * │▏ [⧉ diff] [VS Code] [VS]        [✓ read] │  ← on hover, over the footer
 * └──────────────────────────────────────────┘
 * ```
 *
 * **The row appears on hover and stays while the file is open.** Three hundred permanent button rows
 * would be three hundred rows competing with the thing the diagram is for, and the box has four lines
 * of information already; a row that arrives when the pointer does costs nothing until it is wanted.
 * The box being *current* is the exception — the file the reviewer is reading keeps its controls
 * where they last saw them.
 *
 * **Every button stops the click.** The surface turns a click on a box into its explanation card, so a
 * button that let its click through would open a diff and drop a card over it in the same gesture.
 * Each action also closes whatever card is already open, for the same reason.
 */
export function NodeActions({
  node,
  facts,
  isReviewed,
  isCurrent,
}: {
  node: AnalysisNodeInfo;
  facts: ChangedFileFactsInfo | undefined;
  isReviewed: boolean;
  isCurrent: boolean;
}) {
  const actions = useGraphActions();
  if (!actions) return null;

  const choices = editorChoices(actions.editors);

  return (
    <div
      // `nodrag`/`nopan` are React Flow's own opt-outs. Nothing here is draggable today, but a box
      // whose buttons pan the canvas when pressed is one prop change away without them.
      className={cn(
        'nodrag nopan absolute inset-x-0 bottom-0 flex items-center gap-1 border-t border-border bg-card/95 px-1.5 py-1',
        'transition-opacity focus-within:opacity-100 group-hover:opacity-100',
        isCurrent ? 'opacity-100' : 'pointer-events-none opacity-0 group-hover:pointer-events-auto',
      )}
      data-testid={`node-actions-${node.id}`}
      aria-label={t('analysis.graph.nodeActions', { file: node.filePath })}
      role="group"
    >
      <Action
        label={t('analysis.graph.openDiff')}
        testId={`node-open-diff-${node.id}`}
        onClick={() => actions.openDiff(node)}
      >
        <FileDiffIcon className="size-3" aria-hidden />
      </Action>

      {choices.map(({ editor, shortKey }) => (
        <Action
          key={editor}
          label={t('analysis.diff.openIn', { editor: t(shortKey) })}
          testId={`node-open-in-${editor}-${node.id}`}
          onClick={() => actions.openInEditor(node, facts, editor)}
        >
          <ExternalLinkIcon className="size-3" aria-hidden />
          <span className="text-[9px] font-medium">{t(shortKey)}</span>
        </Action>
      ))}

      <Action
        label={t(isReviewed ? 'analysis.diff.markUnreviewed' : 'analysis.diff.markReviewed')}
        testId={`node-toggle-reviewed-${node.id}`}
        pressed={isReviewed}
        className="ml-auto"
        onClick={() => actions.toggleReviewed(node, !isReviewed)}
      >
        {isReviewed ? (
          <CircleCheckBigIcon className="size-3" aria-hidden />
        ) : (
          <CircleIcon className="size-3" aria-hidden />
        )}
      </Action>
    </div>
  );
}

/** One button on a box: an icon, an accessible name, and a click that goes no further. */
function Action({
  label,
  testId,
  pressed,
  className,
  onClick,
  children,
}: {
  label: string;
  testId: string;
  pressed?: boolean;
  className?: string;
  onClick(): void;
  children: ReactNode;
}) {
  const handle = (event: MouseEvent) => {
    // Both, and both matter: `stopPropagation` keeps React Flow's own node handler from pinning a
    // card over what is about to open, and `preventDefault` keeps the box from taking focus and
    // leaving a ring on a node the reviewer only pressed a button on.
    event.stopPropagation();
    event.preventDefault();
    onClick();
  };

  return (
    <button
      type="button"
      onClick={handle}
      onDoubleClick={(event) => event.stopPropagation()}
      onMouseDown={(event) => event.stopPropagation()}
      aria-label={label}
      title={label}
      aria-pressed={pressed}
      data-testid={testId}
      className={cn(
        'flex items-center gap-1 rounded border border-border bg-background px-1.5 py-0.5 hover:bg-accent',
        pressed && 'border-primary bg-primary/10',
        className,
      )}
    >
      {children}
    </button>
  );
}
