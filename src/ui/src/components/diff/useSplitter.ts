import { useCallback, useEffect, useRef, useState } from 'react';
import { DIFF_PANEL_MIN_GRAPH, DIFF_PANEL_MIN_WIDTH } from '@/store/appStore';

export interface Splitter {
  /** Attach to the drag handle's `onPointerDown`. */
  readonly onPointerDown: (event: React.PointerEvent) => void;

  /** True while a drag is in progress, so the handle can show it and the editor can stop relaying out. */
  readonly dragging: boolean;
}

/**
 * A draggable divider, in about fifty lines and no dependency.
 *
 * `react-resizable-panels` and the shadcn wrapper around it exist. Neither is here, for the same
 * reason the layout worker and the hover-timing hook are hand-written: this is one pointer capture,
 * one clamp and one callback, and a dependency would be more code to read, not less.
 *
 * The clamp is the interesting part, and it is requirement 10 rather than a nicety. `DIFF_PANEL_MIN_GRAPH`
 * keeps a rail of diagram on screen at the widest the panel goes, because the iteration asks for the
 * current position to be visible **in the graph** at all times — "including while the diff panel is
 * expanded to full width". A panel that covered the diagram would satisfy the sentence about full
 * width by breaking the sentence about position.
 */
export function useSplitter(
  container: React.RefObject<HTMLElement | null>,
  width: number,
  onResize: (width: number) => void,
): Splitter {
  const [dragging, setDragging] = useState(false);

  // The callback in a ref so the pointer listeners below are attached once per drag rather than on
  // every render during one — at sixty moves a second, re-attaching is the thing that stutters.
  const resize = useRef(onResize);
  resize.current = onResize;

  const onPointerDown = useCallback(
    (event: React.PointerEvent) => {
      event.preventDefault();
      setDragging(true);
    },
    [],
  );

  useEffect(() => {
    if (!dragging) return;

    const onMove = (event: PointerEvent) => {
      const bounds = container.current?.getBoundingClientRect();
      if (!bounds) return;

      // The panel is on the right, so its width is whatever is left of the container's right edge.
      const proposed = bounds.right - event.clientX;
      const widest = Math.max(DIFF_PANEL_MIN_WIDTH, bounds.width - DIFF_PANEL_MIN_GRAPH);

      resize.current(Math.min(Math.max(proposed, DIFF_PANEL_MIN_WIDTH), widest));
    };

    const stop = () => setDragging(false);

    // On the window rather than on the handle: a pointer that outruns the divider mid-drag would
    // otherwise stop resizing the moment it left the four pixels it started on.
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', stop);
    window.addEventListener('pointercancel', stop);

    // Without this, dragging across the code selects it, and the reviewer ends a resize with half
    // the diff highlighted.
    const previousSelect = document.body.style.userSelect;
    const previousCursor = document.body.style.cursor;
    document.body.style.userSelect = 'none';
    document.body.style.cursor = 'col-resize';

    return () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', stop);
      window.removeEventListener('pointercancel', stop);
      document.body.style.userSelect = previousSelect;
      document.body.style.cursor = previousCursor;
    };
  }, [dragging, container]);

  // A window that shrank below what the current width allows: re-clamp rather than let the panel
  // push the diagram off the screen entirely.
  useEffect(() => {
    const element = container.current;
    if (!element || typeof ResizeObserver === 'undefined') return;

    const observer = new ResizeObserver(([entry]) => {
      if (!entry) return;

      const available = entry.contentRect.width;
      const widest = Math.max(DIFF_PANEL_MIN_WIDTH, available - DIFF_PANEL_MIN_GRAPH);

      if (width > widest) resize.current(widest);
    });

    observer.observe(element);
    return () => observer.disconnect();
  }, [container, width]);

  return { onPointerDown, dragging };
}
