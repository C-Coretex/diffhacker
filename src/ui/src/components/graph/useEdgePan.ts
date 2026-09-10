import { useEffect, type RefObject } from 'react';
import { useReactFlow } from '@xyflow/react';

/**
 * How far the pointer has to travel before a press on a line counts as a drag rather than a click.
 *
 * The same idea as React Flow's own `paneClickDistance`: a hand that moves two pixels while pressing
 * a mouse button meant to click, and losing the edge's card to that would be worse than the bug this
 * hook exists to fix.
 */
const DRAG_THRESHOLD_PX = 3;

/**
 * Panning the diagram from a press that landed on an edge.
 *
 * **Why this exists.** React Flow puts its `nopan` class on *every* edge group, unconditionally
 * (`EdgeWrapper` in `@xyflow/react`), and d3-zoom's filter rejects any mousedown whose target sits
 * inside something carrying that class. On a diagram of three hundred boxes the lines are most of the
 * canvas, and Iteration 9 deliberately made them wider than they look — `interactionWidth` is 20 and
 * 28 pixels — so the practical effect was that dragging the diagram failed wherever the reviewer
 * happened to grab it. The class is not configurable away: whatever `noPanClassName` is set to, the
 * library writes it onto the edge.
 *
 * So the drag is handled here instead, and it is deliberately the *same* gesture d3 performs —
 * translate the viewport by the pointer's delta, zoom untouched — started from the viewport as it was
 * when the button went down, so a long drag cannot accumulate rounding drift. Everything else about
 * panning is still React Flow's; this only covers the presses the library refuses.
 *
 * A press that turns into a drag also swallows the `click` that follows it, or letting go would open
 * the card of whichever line the reviewer happened to start from.
 */
export function useEdgePan(surface: RefObject<HTMLElement | null>): void {
  const { getViewport, setViewport } = useReactFlow();

  useEffect(() => {
    const element = surface.current;
    if (!element) return;

    /** Torn down if the diagram unmounts mid-drag, which a grouping switch can do. */
    let endDrag: (() => void) | null = null;

    const onPointerDown = (event: PointerEvent) => {
      // Left button only, and only where the library would have refused. A press anywhere else on
      // the canvas is already d3's, and running both would pan the diagram twice as fast.
      if (event.button !== 0) return;
      if (!(event.target instanceof Element)) return;
      if (!event.target.closest('.react-flow__edge')) return;

      const start = getViewport();
      const originX = event.clientX;
      const originY = event.clientY;
      let dragging = false;

      const onPointerMove = (move: PointerEvent) => {
        const dx = move.clientX - originX;
        const dy = move.clientY - originY;

        if (!dragging && Math.abs(dx) < DRAG_THRESHOLD_PX && Math.abs(dy) < DRAG_THRESHOLD_PX) {
          return;
        }

        if (!dragging) {
          dragging = true;
          element.style.userSelect = 'none';
        }

        void setViewport({ x: start.x + dx, y: start.y + dy, zoom: start.zoom });
      };

      const finish = () => {
        window.removeEventListener('pointermove', onPointerMove);
        window.removeEventListener('pointerup', finish);
        window.removeEventListener('pointercancel', finish);
        endDrag = null;

        if (!dragging) return;

        element.style.userSelect = '';

        // The click is already on its way to the edge group and would be read as "keep this line's
        // card". Caught in the capture phase, and taken back on the next tick whether or not it
        // arrived — a press released outside the window produces no click at all.
        window.addEventListener('click', swallowClick, true);
        setTimeout(() => window.removeEventListener('click', swallowClick, true), 0);
      };

      endDrag = finish;
      window.addEventListener('pointermove', onPointerMove);
      window.addEventListener('pointerup', finish);
      window.addEventListener('pointercancel', finish);
    };

    element.addEventListener('pointerdown', onPointerDown);

    return () => {
      element.removeEventListener('pointerdown', onPointerDown);
      endDrag?.();
    };
  }, [surface, getViewport, setViewport]);
}

function swallowClick(event: MouseEvent): void {
  event.stopPropagation();
  event.preventDefault();
}
