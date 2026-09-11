import { useCallback, useEffect, useRef, useState } from 'react';
import { DIFF_EXPLANATION_MIN_CODE, DIFF_EXPLANATION_MIN_HEIGHT } from '@/store/appStore';
import type { Splitter } from './useSplitter';

/**
 * The vertical twin of {@link useSplitter}: a draggable divider between the code and the
 * explanation under it, in the same fifty lines and for the same reason — this is one pointer
 * capture, one clamp and one callback, not a dependency.
 *
 * The clamp keeps `DIFF_EXPLANATION_MIN_CODE` of Monaco on screen at the explanation's tallest, the
 * same way the horizontal splitter keeps a rail of diagram at the panel's widest: a reviewer who
 * drags for more room to read prose should not be able to drag the code away entirely.
 */
export function useVerticalSplitter(
  container: React.RefObject<HTMLElement | null>,
  height: number,
  onResize: (height: number) => void,
): Splitter {
  const [dragging, setDragging] = useState(false);

  const resize = useRef(onResize);
  resize.current = onResize;

  const onPointerDown = useCallback((event: React.PointerEvent) => {
    event.preventDefault();
    setDragging(true);
  }, []);

  useEffect(() => {
    if (!dragging) return;

    const onMove = (event: PointerEvent) => {
      const bounds = container.current?.getBoundingClientRect();
      if (!bounds) return;

      // The panel is at the bottom, so its height is whatever is below the pointer.
      const proposed = bounds.bottom - event.clientY;
      const tallest = Math.max(DIFF_EXPLANATION_MIN_HEIGHT, bounds.height - DIFF_EXPLANATION_MIN_CODE);

      resize.current(Math.min(Math.max(proposed, DIFF_EXPLANATION_MIN_HEIGHT), tallest));
    };

    const stop = () => setDragging(false);

    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', stop);
    window.addEventListener('pointercancel', stop);

    const previousSelect = document.body.style.userSelect;
    const previousCursor = document.body.style.cursor;
    document.body.style.userSelect = 'none';
    document.body.style.cursor = 'row-resize';

    return () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', stop);
      window.removeEventListener('pointercancel', stop);
      document.body.style.userSelect = previousSelect;
      document.body.style.cursor = previousCursor;
    };
  }, [dragging, container]);

  // A container that shrank below what the current height allows: re-clamp rather than let the
  // explanation push the code out of view entirely.
  useEffect(() => {
    const element = container.current;
    if (!element || typeof ResizeObserver === 'undefined') return;

    const observer = new ResizeObserver(([entry]) => {
      if (!entry) return;

      const available = entry.contentRect.height;
      const tallest = Math.max(DIFF_EXPLANATION_MIN_HEIGHT, available - DIFF_EXPLANATION_MIN_CODE);

      if (height > tallest) resize.current(tallest);
    });

    observer.observe(element);
    return () => observer.disconnect();
  }, [container, height]);

  return { onPointerDown, dragging };
}
