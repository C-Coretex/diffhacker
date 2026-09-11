import { AlertTriangleIcon, KeyRoundIcon } from 'lucide-react';
import type { EnvironmentInfoSecretBackend } from '@/contracts';
import { useT } from '@/i18n/useT';
import { useAppStore } from '@/store/appStore';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';

const backendKeys = {
  windows_dpapi: 'environment.backend.windows_dpapi',
  macos_keychain: 'environment.backend.macos_keychain',
  linux_libsecret: 'environment.backend.linux_libsecret',
  machine_derived: 'environment.backend.machine_derived',
} as const satisfies Record<EnvironmentInfoSecretBackend, string>;

/**
 * The blocking condition, at the top of every screen.
 *
 * Requirement 6: without git the application is non-functional, so this says so once, plainly,
 * rather than letting the user discover it by picking a repository and getting a confusing
 * failure.
 */
export function GitMissingBanner() {
  const t = useT();
  const environment = useAppStore((state) => state.environment);
  const info = useAppStore((state) => state.environmentInfo);

  if (environment !== 'ready' || info?.gitAvailable !== false) {
    return null;
  }

  return (
    <Alert variant="destructive" role="alert">
      <AlertTriangleIcon />
      <AlertTitle>{t('environment.gitMissingHeading')}</AlertTitle>
      <AlertDescription>
        <p>{t('environment.gitMissingBody')}</p>
        <p className="text-xs opacity-80">{t('environment.gitMissingHint')}</p>
      </AlertDescription>
    </Alert>
  );
}

/**
 * Prompts to add an LLM provider when none is configured yet.
 *
 * Every run — analysis or profile — needs a provider, so a reviewer who has never opened
 * Settings should be told before they discover it as a run failure. Dismissible rather than
 * blocking: the reviewer may only want to look at the changed-file list first, and the reactive
 * `error.analysis_no_provider` / `error.profile_no_provider` messages still catch anyone who goes
 * on to run something anyway. Hidden on the Settings screen itself, which already shows its own
 * empty state for this.
 */
export function NoProviderBanner() {
  const t = useT();
  const status = useAppStore((state) => state.providers);
  const profiles = useAppStore((state) => state.providerProfiles);
  const dismissed = useAppStore((state) => state.providersPromptDismissed);
  const dismiss = useAppStore((state) => state.dismissProvidersPrompt);
  const screen = useAppStore((state) => state.screen);
  const showScreen = useAppStore((state) => state.showScreen);

  if (status !== 'ready' || profiles.length > 0 || dismissed || screen === 'settings') {
    return null;
  }

  return (
    <Alert variant="warning" role="status">
      <AlertTriangleIcon />
      <AlertTitle>{t('providers.noProviderHeading')}</AlertTitle>
      <AlertDescription>
        <p>{t('providers.noProviderBody')}</p>
        <div className="flex gap-2 pt-1">
          <Button size="sm" onClick={() => showScreen('settings')}>
            {t('providers.noProviderAction')}
          </Button>
          <Button size="sm" variant="ghost" onClick={dismiss}>
            {t('providers.noProviderDismiss')}
          </Button>
        </div>
      </AlertDescription>
    </Alert>
  );
}

/**
 * States which store is actually protecting API keys, and warns when it is the fallback.
 *
 * The fallback is a genuinely weaker promise than a system keyring, so the interface says so
 * rather than claiming a keyring that is not there.
 */
export function SecretBackendNotice() {
  const t = useT();
  const info = useAppStore((state) => state.environmentInfo);

  if (!info) {
    return null;
  }

  if (!info.secretBackendIsFallback) {
    return (
      <p className="text-muted-foreground flex items-center gap-2 text-xs">
        <KeyRoundIcon className="size-3.5 shrink-0" aria-hidden />
        {t('environment.secretBackend')} {t(backendKeys[info.secretBackend])}.
      </p>
    );
  }

  return (
    <Alert variant="warning">
      <AlertTriangleIcon />
      <AlertTitle>
        {t('environment.secretBackend')} {t(backendKeys[info.secretBackend])}
      </AlertTitle>
      <AlertDescription>{t('environment.fallbackWarning')}</AlertDescription>
    </Alert>
  );
}
