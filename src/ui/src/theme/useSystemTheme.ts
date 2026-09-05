import { useEffect, useState } from 'react';

export type Theme = 'light' | 'dark';

const QUERY = '(prefers-color-scheme: dark)';

/**
 * Follows the operating system's colour scheme, live.
 *
 * Tailwind's dark variant is wired to a `dark` class on `<html>` rather than to the media
 * query directly, so the class has to be kept in step here. Switching the OS theme updates the
 * interface without a restart.
 */
export function useSystemTheme(): Theme {
  const [theme, setTheme] = useState<Theme>(() => detect());

  useEffect(() => {
    const media = window.matchMedia?.(QUERY);
    if (!media) {
      return;
    }

    const onChange = (event: MediaQueryListEvent) => setTheme(event.matches ? 'dark' : 'light');
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, []);

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
    document.documentElement.style.colorScheme = theme;
  }, [theme]);

  return theme;
}

function detect(): Theme {
  return window.matchMedia?.(QUERY).matches ?? false ? 'dark' : 'light';
}
