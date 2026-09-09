import { useEffect, useState } from 'react';
import { useAppStore } from '@/store/appStore';

/** The two things the interface can actually look like. */
export type Theme = 'light' | 'dark';

/** What the user asked for, which is not the same thing: "system" resolves to one of the two. */
export type ThemePreference = 'system' | Theme;

const QUERY = '(prefers-color-scheme: dark)';

/**
 * Where the choice is remembered.
 *
 * `localStorage`, not SQLite through the host. Every other setting goes to the host because it is
 * either a secret, a piece of repository state, or something the analysis needs; a colour scheme is
 * none of those. It is per-window presentation, it must be applied on the very first paint before
 * any bridge call has resolved, and putting it behind a JSON-RPC round trip would mean a schema, a
 * method, a handler and a table for a value that is one word long. §0.2.13's "pure renderer" rule
 * is about network, filesystem and secrets — browser storage is none of those either.
 */
const STORAGE_KEY = 'diffhacker.theme';

/** Reads the remembered preference. Falls back to following the OS, which is what it did before. */
export function storedPreference(): ThemePreference {
  try {
    const stored = window.localStorage?.getItem(STORAGE_KEY);
    return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system';
  } catch {
    // A browser with site data blocked, or a context where storage throws on access rather than
    // returning null. Following the OS is a perfectly good answer.
    return 'system';
  }
}

export function rememberPreference(preference: ThemePreference): void {
  try {
    window.localStorage?.setItem(STORAGE_KEY, preference);
  } catch {
    // The choice still applies for this session; it just will not survive a restart.
  }
}

/** What the operating system is asking for, right now. */
export function systemTheme(): Theme {
  return window.matchMedia?.(QUERY).matches ?? false ? 'dark' : 'light';
}

/**
 * Resolves the user's choice against the operating system, and keeps the document in step.
 *
 * Tailwind's dark variant is wired to a `dark` class on `<html>` rather than to the media query
 * directly, so the class has to be set here whichever way the answer came from — and the media
 * query is still watched even when the preference is explicit, because switching back to "system"
 * has to be correct immediately rather than after the next OS change.
 */
export function useTheme(): Theme {
  const preference = useAppStore((state) => state.themePreference);
  const [system, setSystem] = useState<Theme>(() => systemTheme());

  useEffect(() => {
    const media = window.matchMedia?.(QUERY);
    if (!media) {
      return;
    }

    const onChange = (event: MediaQueryListEvent) => setSystem(event.matches ? 'dark' : 'light');
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, []);

  const theme = preference === 'system' ? system : preference;

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
    document.documentElement.style.colorScheme = theme;
  }, [theme]);

  return theme;
}
