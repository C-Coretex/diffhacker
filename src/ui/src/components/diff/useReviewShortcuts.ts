import { useEffect } from 'react';
import type { AnalysisView } from '@/contracts';
import { useAppStore } from '@/store/appStore';
import { readingPosition } from './neighbours';
import { useReviewMarks } from './useReviewMarks';

/**
 * Requirement 10: shortcuts where they are cheap, and nothing beyond that.
 *
 * | | |
 * |---|---|
 * | `j` / `k` | next and previous in the recommended reading order |
 * | `Enter` | open the diff for whatever the diagram is focused on |
 * | `r` | mark the open node reviewed, or unmark it |
 * | `c` | fold the open node's cluster |
 * | `Escape` | leave full screen, or close the panel |
 *
 * Deliberately small. §0.6 makes keyboard navigation "a nice-to-have, not a priority" and
 * `future-improvements.md` keeps full keyboard-driven review as its own piece of work — so this is the
 * handful that costs a `keydown` listener, not the beginning of a modal editor.
 *
 * Single unmodified keys, which is only safe because of the guard below: anything typed into a field,
 * or into Monaco, belongs to that field. Monaco is the interesting one — it is a `contenteditable`
 * region, so `isContentEditable` is what keeps `j` from being swallowed while the reviewer is
 * selecting code, and `r` from marking a file reviewed while they are searching inside it.
 */
export function useReviewShortcuts(view: AnalysisView): void {
  const nodeId = useAppStore((state) => state.diffNodeId);
  const focusedId = useAppStore((state) => state.graphFocusedNodeId);
  const openDiff = useAppStore((state) => state.openDiffFor);
  const closeDiff = useAppStore((state) => state.closeDiff);
  const toggleContainer = useAppStore((state) => state.toggleContainerCollapsed);
  const fullScreen = useAppStore((state) => state.diffFullScreen);
  const setFullScreen = useAppStore((state) => state.setDiffFullScreen);

  const marks = useReviewMarks(view.repositoryPath);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.metaKey || event.ctrlKey || event.altKey || isTyping(event.target)) return;

      const current = nodeId ?? focusedId;
      const node = current ? view.nodes.find((candidate) => candidate.id === current) : undefined;

      const step = (direction: -1 | 1) => {
        const reading = readingPosition(view, current ?? '');

        // Nothing open yet: j starts at the beginning of the recommended order, which is the most
        // useful thing an empty panel can do with a keypress.
        const targetId =
          reading.position === 0
            ? (view.readingOrder[0] ?? null)
            : direction === 1
              ? reading.nextId
              : reading.previousId;

        const target = targetId
          ? view.nodes.find((candidate) => candidate.id === targetId)
          : undefined;

        if (target) openDiff(target.id, target.containerId);
      };

      switch (event.key) {
        case 'j':
          event.preventDefault();
          step(1);
          break;

        case 'k':
          event.preventDefault();
          step(-1);
          break;

        case 'Enter':
          if (!node) break;
          event.preventDefault();
          openDiff(node.id, node.containerId);
          break;

        case 'r':
          if (!node) break;
          event.preventDefault();
          marks.mark([node.id], !marks.isReviewed(node.id));
          break;

        case 'c':
          if (!node) break;
          event.preventDefault();
          toggleContainer(node.containerId);
          break;

        // One step back at a time. Escape out of full screen puts the diagram back; Escape again
        // closes the panel. Collapsing the two would make one keypress undo two decisions.
        case 'Escape':
          if (!nodeId) break;
          event.preventDefault();
          if (fullScreen) setFullScreen(false);
          else closeDiff();
          break;

        default:
          break;
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [view, nodeId, focusedId, openDiff, closeDiff, toggleContainer, marks, fullScreen, setFullScreen]);
}

/** Whether the keypress belongs to something the reviewer is typing into. */
function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;

  return (
    target.isContentEditable ||
    target instanceof HTMLInputElement ||
    target instanceof HTMLTextAreaElement ||
    target instanceof HTMLSelectElement
  );
}
