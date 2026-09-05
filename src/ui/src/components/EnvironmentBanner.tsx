import { AlertTriangleIcon, KeyRoundIcon } from 'lucide-react';
import type { EnvironmentInfoSecretBackend } from '@/contracts';
import { useT } from '@/i18n/useT';
import { useAppStore } from '@/store/appStore';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';

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
