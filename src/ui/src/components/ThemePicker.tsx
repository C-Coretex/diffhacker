import { MonitorIcon, MoonIcon, SunIcon } from 'lucide-react';
import type { ResourceKey } from '@/i18n/translate';
import { useT } from '@/i18n/useT';
import { cn } from '@/lib/utils';
import { useAppStore } from '@/store/appStore';
import type { ThemePreference } from '@/theme/useTheme';

/**
 * Light, dark, or follow the operating system.
 *
 * Three buttons rather than one that cycles: cycling makes the current state something you infer
 * from the icon and the next state something you guess at, and there are only three of them. Each
 * carries a label as well as an icon, because an icon-only control is one nobody finds when they
 * are looking for a setting rather than for a picture.
 *
 * In the application header rather than on the settings screen, because it is the one preference
 * whose effect is immediate and whose right answer depends on the room you are sitting in.
 */
export function ThemePicker() {
  const t = useT();
  const preference = useAppStore((state) => state.themePreference);
  const setPreference = useAppStore((state) => state.setThemePreference);

  return (
    <div
      role="group"
      aria-label={t('theme.label')}
      className="flex items-center gap-0.5 rounded-md border border-border p-0.5"
    >
      {OPTIONS.map(({ value, label, Icon }) => (
        <button
          key={value}
          type="button"
          onClick={() => setPreference(value)}
          aria-pressed={preference === value}
          aria-label={t(label)}
          title={t(label)}
          className={cn(
            'flex size-7 items-center justify-center rounded text-muted-foreground hover:bg-accent hover:text-foreground',
            preference === value && 'bg-accent text-foreground',
          )}
        >
          <Icon className="size-4" aria-hidden />
        </button>
      ))}
    </div>
  );
}

const OPTIONS: {
  value: ThemePreference;
  label: ResourceKey;
  Icon: typeof SunIcon;
}[] = [
  { value: 'system', label: 'theme.system', Icon: MonitorIcon },
  { value: 'light', label: 'theme.light', Icon: SunIcon },
  { value: 'dark', label: 'theme.dark', Icon: MoonIcon },
];
