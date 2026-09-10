import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { useHoverTarget, type HoverTarget } from './useHoverTarget';

function target(id: string): HoverTarget {
  return { kind: 'node', id, rect: new DOMRect(0, 0, 260, 96) };
}

describe('useHoverTarget', () => {
  it('opens a card at once when something is clicked', () => {
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.toggle(target('a')));
    expect(result.current.target?.id).toBe('a');
  });

  it('moves the card to a different thing rather than refusing', () => {
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.toggle(target('a')));
    act(() => result.current.toggle(target('b')));

    expect(result.current.target?.id).toBe('b');
  });

  it('closes the card when the same thing is clicked again', () => {
    // The gesture that opened it is the one a hand reaches for to close it.
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.toggle(target('a')));
    act(() => result.current.toggle(target('a')));

    expect(result.current.target).toBeNull();

    // And a third click opens it again rather than leaving it stuck shut.
    act(() => result.current.toggle(target('a')));
    expect(result.current.target?.id).toBe('a');
  });

  it('tells a node from its container even when the ids match', () => {
    // Ids come from different namespaces — a cluster called `src` and a file called `src` are not
    // the same thing, and clicking one must not close the other's card.
    const { result } = renderHook(() => useHoverTarget());
    const rect = new DOMRect(0, 0, 260, 96);

    act(() => result.current.toggle({ kind: 'node', id: 'src', rect }));
    act(() => result.current.toggle({ kind: 'container', id: 'src', rect }));

    expect(result.current.target?.kind).toBe('container');
  });

  it('closes whatever is open, however it got there', () => {
    const { result } = renderHook(() => useHoverTarget());

    act(() => result.current.toggle(target('a')));
    act(() => result.current.close());

    expect(result.current.target).toBeNull();
  });
});
