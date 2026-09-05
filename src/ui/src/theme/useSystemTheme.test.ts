import { renderHook, act } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useSystemTheme } from './useSystemTheme';

/** A controllable stand-in for `window.matchMedia`, so OS theme changes can be simulated. */
function installMatchMedia(initialMatches: boolean) {
  const listeners = new Set<(event: MediaQueryListEvent) => void>();
  let matches = initialMatches;

  window.matchMedia = ((query: string) => ({
    get matches() {
      return matches;
    },
    media: query,
    onchange: null,
    addEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => listeners.add(listener),
    removeEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) => listeners.delete(listener),
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia;

  return {
    emit(next: boolean) {
      matches = next;
      for (const listener of listeners) {
        listener({ matches: next } as MediaQueryListEvent);
      }
    },
    get listenerCount() {
      return listeners.size;
    },
  };
}

describe('useSystemTheme', () => {
  beforeEach(() => {
    document.documentElement.classList.remove('dark');
  });

  afterEach(() => {
    document.documentElement.classList.remove('dark');
  });

  it('starts from the operating system preference', () => {
    installMatchMedia(true);

    const { result } = renderHook(() => useSystemTheme());

    expect(result.current).toBe('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
  });

  it('follows a theme change without a restart', () => {
    const media = installMatchMedia(false);
    const { result } = renderHook(() => useSystemTheme());

    expect(result.current).toBe('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);

    act(() => media.emit(true));

    expect(result.current).toBe('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
    expect(document.documentElement.style.colorScheme).toBe('dark');

    act(() => media.emit(false));

    expect(result.current).toBe('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });

  it('detaches its listener on unmount', () => {
    const media = installMatchMedia(false);
    const { unmount } = renderHook(() => useSystemTheme());

    expect(media.listenerCount).toBe(1);
    unmount();
    expect(media.listenerCount).toBe(0);
  });
});
