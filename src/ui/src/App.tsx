import { useEffect } from 'react';
import { SettingsIcon } from 'lucide-react';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { useRpc } from '@/rpc/RpcProvider';
import { describeEnvironment, ping, reportSelfTest } from '@/rpc/methods';
import { runSelfTest } from '@/selfTest/runSelfTest';
import { useAppStore } from '@/store/appStore';
import { useSystemTheme } from '@/theme/useSystemTheme';
import { Button } from '@/components/ui/button';
import { GitMissingBanner } from '@/components/EnvironmentBanner';
import { WelcomeScreen } from '@/components/WelcomeScreen';
import { RepositoryScreen } from '@/components/RepositoryScreen';
import { SettingsScreen } from '@/components/SettingsScreen';
import { HostPanel } from '@/components/HostPanel';

export function App() {
  const t = useT();
  useSystemTheme();

  const client = useRpc();
  const connection = useAppStore((state) => state.connection);
  const screen = useAppStore((state) => state.screen);
  const showScreen = useAppStore((state) => state.showScreen);
  const setConnected = useAppStore((state) => state.setConnected);
  const setDetached = useAppStore((state) => state.setDetached);
  const setConnectionError = useAppStore((state) => state.setConnectionError);
  const setEnvironment = useAppStore((state) => state.setEnvironment);
  const failEnvironment = useAppStore((state) => state.failEnvironment);

  useEffect(() => {
    if (!client) {
      setDetached();
      return;
    }

    let cancelled = false;

    void (async () => {
      try {
        const hostInfo = await ping(client);
        if (cancelled) {
          return;
        }

        setConnected(hostInfo);

        // Probed once, up front: without git the application is non-functional, and saying so
        // immediately beats letting the user discover it at their first repository.
        try {
          const environment = await describeEnvironment(client);
          if (!cancelled) {
            setEnvironment(environment);
          }
        } catch (error) {
          if (!cancelled) {
            failEnvironment(describeError(error));
          }
        }

        // CI launches the host with --self-test. The renderer proves the bridge works and
        // reports back; the host turns that verdict into a process exit code.
        if (hostInfo.selfTest) {
          const result = await runSelfTest(client, hostInfo);
          if (!cancelled) {
            await reportSelfTest(client, result);
          }
        }
      } catch (error) {
        if (!cancelled) {
          setConnectionError(describeError(error));
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [client, setConnected, setDetached, setConnectionError, setEnvironment, failEnvironment]);

  return (
    <div className="flex h-screen flex-col overflow-hidden">
      <header className="flex shrink-0 items-start justify-between gap-4 border-b px-8 py-6">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">{t('app.title')}</h1>
          <p className="text-muted-foreground mt-1 text-sm">{t('app.tagline')}</p>
        </div>

        <nav className="flex items-center gap-2">
          {screen === 'settings' ? (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => showScreen(useAppStore.getState().repositoryInfo ? 'repository' : 'welcome')}
            >
              {t('app.nav.back')}
            </Button>
          ) : (
            <Button variant="ghost" size="sm" onClick={() => showScreen('settings')}>
              <SettingsIcon aria-hidden />
              {t('app.nav.settings')}
            </Button>
          )}
        </nav>
      </header>

      <main className="flex-1 overflow-auto px-8 py-6">
        <div className="mx-auto flex max-w-3xl flex-col gap-6">
          <GitMissingBanner />

          {connection !== 'connected' && <HostPanel />}

          {screen === 'welcome' && <WelcomeScreen />}
          {screen === 'repository' && <RepositoryScreen />}
          {screen === 'settings' && <SettingsScreen />}
        </div>
      </main>
    </div>
  );
}
