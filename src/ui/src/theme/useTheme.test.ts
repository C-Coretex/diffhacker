import { renderHook, act } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useAppStore } from '@/store/appStore';
import { storedPreference, useTheme } from './useTheme';

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

const isDark = () => document.documentElement.classList.contains('dark');

describe('useTheme', () => {
  beforeEach(() => {
    document.documentElement.classList.remove('dark');
    window.localStorage.clear();
    useAppStore.setState({ themePreference: 'system' });
  });

  afterEach(() => {
    document.documentElement.classList.remove('dark');
    window.localStorage.clear();
  });

  it('follows the operating system when nothing has been chosen', () => {
    installMatchMedia(true);

    const { result } = renderHook(() => useTheme());

    expect(result.current).toBe('dark');
    expect(isDark()).toBe(true);
  });

  it('follows a system theme change without a restart', () => {
    const media = installMatchMedia(false);
    const { result } = renderHook(() => useTheme());

    expect(result.current).toBe('light');
    expect(isDark()).toBe(false);

    act(() => media.emit(true));

    expect(result.current).toBe('dark');
    expect(isDark()).toBe(true);
    expect(document.documentElement.style.colorScheme).toBe('dark');

    act(() => media.emit(false));

    expect(result.current).toBe('light');
    expect(isDark()).toBe(false);
  });

  it('lets an explicit choice override the operating system', () => {
    const media = installMatchMedia(true);
    const { result } = renderHook(() => useTheme());

    expect(result.current).toBe('dark');

    act(() => useAppStore.getState().setThemePreference('light'));

    expect(result.current).toBe('light');
    expect(isDark()).toBe(false);

    // And the OS changing underneath no longer moves it: the user said light.
    act(() => media.emit(false));
    act(() => media.emit(true));

    expect(result.current).toBe('light');
    expect(isDark()).toBe(false);
  });

  it('goes back to following the system, correct immediately rather than at the next change', () => {
    // The reason the media query is watched even while a preference is explicit: switching back to
    // "system" has to resolve against what the OS is asking for *now*, not against a stale value.
    const media = installMatchMedia(false);
    const { result } = renderHook(() => useTheme());

    act(() => useAppStore.getState().setThemePreference('light'));
    act(() => media.emit(true));
    expect(result.current).toBe('light');

    act(() => useAppStore.getState().setThemePreference('system'));
    expect(result.current).toBe('dark');
    expect(isDark()).toBe(true);
  });

  it('remembers the choice for the next launch', () => {
    installMatchMedia(false);
    renderHook(() => useTheme());

    act(() => useAppStore.getState().setThemePreference('dark'));

    expect(storedPreference()).toBe('dark');
  });

  it('falls back to following the system when nothing readable was stored', () => {
    window.localStorage.setItem('diffhacker.theme', 'sepia');
    expect(storedPreference()).toBe('system');
  });

  it('detaches its listener on unmount', () => {
    const media = installMatchMedia(false);
    const { unmount } = renderHook(() => useTheme());

    expect(media.listenerCount).toBe(1);
    unmount();
    expect(media.listenerCount).toBe(0);
  });
});
