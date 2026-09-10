import { useCallback, useMemo, useState } from 'react';

/** What was clicked, and where on screen it is. */
export interface HoverTarget {
  readonly kind: 'node' | 'edge' | 'container';
  /** Node id, container id, or React Flow edge id. */
  readonly id: string;
  /** The element's viewport rectangle, so the card can be anchored beside it. */
  readonly rect: DOMRect;
}

export interface HoverController {
  readonly target: HoverTarget | null;
  /** Something was clicked: open its card, or close it if it was already open. */
  toggle(target: HoverTarget): void;
  /** Puts whatever card is open away. */
  close(): void;
}

/**
 * The state behind the one explanation card the diagram ever shows.
 *
 * Clicking is the only way in: a node, an edge and a cluster's title bar all call `toggle` with
 * what they are. Clicking the thing whose card is already open closes it — the gesture that opened
 * something is the one a hand reaches for to close it, and a control that only goes one way is one
 * people press twice and then go looking for the exit. Clicking a *different* thing moves the card
 * there rather than being refused.
 *
 * Nothing here times out or auto-closes. A card opened by a click is the reviewer's until they
 * dismiss it — by clicking it again, clicking the background, or pressing Escape — so it survives
 * panning the canvas, collapsing a neighbouring cluster, or reading with the pointer somewhere else
 * entirely. Replacing it because the pointer passed over another box on the way to the scrollbar
 * would be the worst version of this feature.
 */
export function useHoverTarget(): HoverController {
  const [target, setTarget] = useState<HoverTarget | null>(null);

  const close = useCallback(() => setTarget(null), []);

  const toggle = useCallback((next: HoverTarget) => {
    setTarget((current) => (current && identify(current) === identify(next) ? null : next));
  }, []);

  // Memoised so the controller is a stable value: several children hold onto it, and a fresh object
  // on every render would be a fresh prop for all of them to no purpose.
  return useMemo(() => ({ target, toggle, close }), [target, toggle, close]);
}

/**
 * What makes two targets the same thing.
 *
 * The kind as well as the id, because a node and its container can be told apart only by that — a
 * cluster's id and a node's id come from different namespaces and nothing stops them matching.
 */
function identify(target: HoverTarget): string {
  return `${target.kind}:${target.id}`;
}
