import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { useAppStore } from '@/store/appStore';
import { storedPreference } from '@/theme/useTheme';
import { ThemePicker } from './ThemePicker';

describe('ThemePicker', () => {
  beforeEach(() => {
    window.localStorage.clear();
    useAppStore.setState({ themePreference: 'system' });
  });

  it('offers all three choices and shows which one is in force', () => {
    render(<ThemePicker />);

    expect(screen.getByRole('button', { name: 'Follow the system' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByRole('button', { name: 'Light' })).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('button', { name: 'Dark' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('records a choice so the next launch starts with it', async () => {
    render(<ThemePicker />);

    await userEvent.click(screen.getByRole('button', { name: 'Dark' }));

    expect(useAppStore.getState().themePreference).toBe('dark');
    expect(storedPreference()).toBe('dark');
    expect(screen.getByRole('button', { name: 'Dark' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('goes back to following the system', async () => {
    useAppStore.setState({ themePreference: 'light' });
    render(<ThemePicker />);

    await userEvent.click(screen.getByRole('button', { name: 'Follow the system' }));

    expect(useAppStore.getState().themePreference).toBe('system');
    expect(storedPreference()).toBe('system');
  });
});
