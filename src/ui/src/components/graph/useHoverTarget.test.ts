import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CLOSE_DELAY_MS, OPEN_DELAY_MS, useHoverTarget, type HoverTarget } from './useHoverTarget';

function target(id: string): HoverTarget {
  return { kind: 'node', id, rect: new DOMRect(0, 0, 260, 96) };
}

/** Advances fake time inside `act`, so the state the timer sets is flushed before it is read. */
function advance(ms: number): void {
  act(() => {
    vi.advanceTimersByTime(ms);
  });
}

/**
 * The timing behind every hover card.
 *
 * Tested on its own rather than through the diagram because these are the numbers most likely to be
 * argued with later, and because a timing bug in a graph test looks like flakiness rather than like
 * a decision that was changed.
 */
describe('useHoverTarget', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('shows nothing until the pointer has rested', () => {
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.show(target('a')));
    advance(OPEN_DELAY_MS - 50);
    expect(result.current.target).toBeNull();

    advance(50);
    expect(result.current.target?.id).toBe('a');
  });

  it('swaps to the next box with no second wait', () => {
    // The behaviour that decides whether reading across a cluster feels like reading or like
    // arguing: once a card is up, the neighbouring box replaces it at once.
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.show(target('a')));
    advance(OPEN_DELAY_MS);

    act(() => result.current.show(target('b')));
    expect(result.current.target?.id).toBe('b');
  });

  it('survives the pointer leaving for long enough to reach the card', () => {
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.show(target('a')));
    advance(OPEN_DELAY_MS);

    act(() => result.current.hide());
    advance(CLOSE_DELAY_MS - 50);
    expect(result.current.target?.id).toBe('a');

    // The pointer arrived on the card itself.
    act(() => result.current.hold());
    advance(1000);
    expect(result.current.target?.id).toBe('a');
  });

  it('closes once the pointer has been gone for the grace period', () => {
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.show(target('a')));
    advance(OPEN_DELAY_MS);

    act(() => result.current.hide());
    advance(CLOSE_DELAY_MS);
    expect(result.current.target).toBeNull();
  });

  it('keeps a card at once when something is clicked', () => {
    // No delay and no travelling to a button: clicking is how a reviewer says "I want to read this",
    // and making them hold a hand still to do it is the thing hover cards get wrong.
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.pinTo(target('a')));

    expect(result.current.target?.id).toBe('a');
    expect(result.current.pinned).toBe(true);

    // Clicking a second box moves the card rather than being refused: the pin locks out the
    // pointer, not the reviewer.
    act(() => result.current.pinTo(target('b')));

    expect(result.current.target?.id).toBe('b');
    expect(result.current.pinned).toBe(true);
  });

  it('closes the card when the same thing is clicked again', () => {
    // The gesture that opened it is the one a hand reaches for to close it.
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.pinTo(target('a')));
    act(() => result.current.pinTo(target('a')));

    expect(result.current.target).toBeNull();
    expect(result.current.pinned).toBe(false);

    // And a third click opens it again rather than leaving it stuck shut.
    act(() => result.current.pinTo(target('a')));
    expect(result.current.target?.id).toBe('a');
  });

  it('tells a node from its container even when the ids match', () => {
    // Ids come from different namespaces — a cluster called `src` and a file called `src` are not
    // the same thing, and clicking one must not close the other's card.
    const { result } = renderHook(() => useHoverTarget());
    const rect = new DOMRect(0, 0, 260, 96);

    act(() => result.current.pinTo({ kind: 'node', id: 'src', rect }));
    act(() => result.current.pinTo({ kind: 'container', id: 'src', rect }));

    expect(result.current.pinned).toBe(true);
    expect(result.current.target?.kind).toBe('container');
  });

  it('opens rather than closes when a merely hovered card is clicked', () => {
    // Only a *kept* card toggles. Clicking what you are hovering is how you keep it, and having
    // that close the card instead would make the gesture unusable.
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.show(target('a')));
    advance(OPEN_DELAY_MS);
    expect(result.current.pinned).toBe(false);

    act(() => result.current.pinTo(target('a')));

    expect(result.current.pinned).toBe(true);
    expect(result.current.target?.id).toBe('a');
  });

  it('ignores the pointer entirely while pinned', () => {
    // A pinned card is the reviewer's. Replacing it because the pointer crossed another box on the
    // way to its scrollbar would be the worst version of this feature.
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.show(target('a')));
    advance(OPEN_DELAY_MS);
    act(() => result.current.pin());

    act(() => result.current.show(target('b')));
    advance(OPEN_DELAY_MS);
    act(() => result.current.hide());
    advance(CLOSE_DELAY_MS);

    expect(result.current.pinned).toBe(true);
    expect(result.current.target?.id).toBe('a');

    act(() => result.current.unpin());
    expect(result.current.pinned).toBe(false);
    expect(result.current.target).toBeNull();
  });
});
