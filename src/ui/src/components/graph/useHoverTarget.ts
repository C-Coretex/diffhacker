import { useCallback, useEffect, useMemo, useRef, useState } from 'react';

/** What the pointer is on, and where on screen it is. */
export interface HoverTarget {
  readonly kind: 'node' | 'edge' | 'container';
  /** Node id, container id, or React Flow edge id. */
  readonly id: string;
  /** The element's viewport rectangle, so the card can be anchored beside it. */
  readonly rect: DOMRect;
}

/**
 * How long the pointer has to rest on something before its card appears.
 *
 * Long enough that crossing a diagram does not strobe forty cards; short enough that stopping on a
 * box and waiting does not feel like waiting.
 */
export const OPEN_DELAY_MS = 250;

/**
 * How long a card survives the pointer leaving.
 *
 * This is what makes an unpinned card reachable at all: the pointer has to cross the gap between the
 * box and the card to get to the controls on it. 150 ms was the first guess and it was too tight —
 * a hand that does not go straight there loses the card halfway. Clicking is the reliable way to
 * keep one, and this is the forgiving way.
 */
export const CLOSE_DELAY_MS = 500;

export interface HoverController {
  readonly target: HoverTarget | null;
  readonly pinned: boolean;
  /** The pointer arrived on something. */
  show(target: HoverTarget): void;
  /** The pointer left it. */
  hide(): void;
  /** The pointer is on the card itself, so nothing should close. */
  hold(): void;
  /** Something was clicked: show its card at once and keep it. */
  pinTo(target: HoverTarget): void;
  pin(): void;
  unpin(): void;
}

/**
 * The hover state machine behind every card on the diagram.
 *
 * Three behaviours, and each one is there because its absence is the thing that would make hovering
 * feel wrong:
 *
 * - **A delay before the first card**, so sweeping the pointer across a cluster shows nothing.
 * - **No delay between adjacent ones.** Once a card is up, moving to the next box swaps it
 *   immediately; re-serving the 250 ms wait for every neighbour is what makes a diagram feel like it
 *   is arguing with you.
 * - **A grace period on leaving**, so the pointer can travel from the box onto the card.
 *
 * And **clicking keeps the card** — `pinTo`. Hovering is for glancing; the moment a reviewer wants
 * to read, scroll or select, they should not have to keep a hand still to do it.
 *
 * A pinned card ignores the pointer entirely. It is the reviewer's now: they asked for it, they are
 * scrolling or selecting text in it, and having it replaced because the pointer passed over another
 * box on the way to the scrollbar would be the worst version of this feature.
 */
export function useHoverTarget(): HoverController {
  const [target, setTarget] = useState<HoverTarget | null>(null);
  const [pinned, setPinned] = useState(false);

  // Refs rather than state: the timers are read inside callbacks that must not be rebuilt on every
  // pointer move, and `openRef` mirrors `target` so `show` can tell "first card" from "next card"
  // without depending on the rendered value.
  const openTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const closeTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const isOpen = useRef(false);
  const isPinned = useRef(false);

  // What the open card is about, mirrored so `pinTo` can tell "clicked the same thing again" from
  // "clicked something else" without being rebuilt every time the pointer moves.
  const shownId = useRef<string | null>(null);

  const clearTimers = useCallback(() => {
    if (openTimer.current !== null) clearTimeout(openTimer.current);
    if (closeTimer.current !== null) clearTimeout(closeTimer.current);
    openTimer.current = null;
    closeTimer.current = null;
  }, []);

  useEffect(() => clearTimers, [clearTimers]);

  const open = useCallback((next: HoverTarget) => {
    isOpen.current = true;
    shownId.current = identify(next);
    setTarget(next);
  }, []);

  const show = useCallback(
    (next: HoverTarget) => {
      if (isPinned.current) return;

      clearTimers();

      if (isOpen.current) {
        open(next);
        return;
      }

      openTimer.current = setTimeout(() => {
        openTimer.current = null;
        open(next);
      }, OPEN_DELAY_MS);
    },
    [clearTimers, open],
  );

  const hide = useCallback(() => {
    if (isPinned.current) return;

    clearTimers();

    closeTimer.current = setTimeout(() => {
      closeTimer.current = null;
      isOpen.current = false;
      shownId.current = null;
      setTarget(null);
    }, CLOSE_DELAY_MS);
  }, [clearTimers]);

  const hold = useCallback(() => {
    if (closeTimer.current !== null) {
      clearTimeout(closeTimer.current);
      closeTimer.current = null;
    }
  }, []);

  const pin = useCallback(() => {
    clearTimers();
    isPinned.current = true;
    setPinned(true);
  }, [clearTimers]);

  const unpin = useCallback(() => {
    clearTimers();
    isPinned.current = false;
    isOpen.current = false;
    shownId.current = null;
    setPinned(false);
    setTarget(null);
  }, [clearTimers]);

  /**
   * Clicking something is the unambiguous way to keep its card — and to put it away again.
   *
   * No delay, no pointer to keep still, and no travelling across a gap to reach a button: the card
   * appears where it was clicked and stays until it is dismissed. Clicking a *different* box while
   * one is kept moves the card there rather than being ignored — the pin locks out the pointer, not
   * the reviewer. Clicking the **same** box again closes it, because the gesture that opened
   * something is the one a hand reaches for to close it, and a control that only goes one way is one
   * people press twice and then go looking for the exit.
   */
  const pinTo = useCallback(
    (next: HoverTarget) => {
      if (isPinned.current && shownId.current === identify(next)) {
        unpin();
        return;
      }

      clearTimers();
      isPinned.current = true;
      isOpen.current = true;
      shownId.current = identify(next);
      setPinned(true);
      setTarget(next);
    },
    [clearTimers, unpin],
  );

  // Memoised so the controller is a stable value: the surface passes it into effects and into a
  // child, and a fresh object on every pointer move would re-run both for nothing.
  return useMemo(
    () => ({ target, pinned, show, hide, hold, pinTo, pin, unpin }),
    [target, pinned, show, hide, hold, pinTo, pin, unpin],
  );
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
