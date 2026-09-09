import { useEffect } from 'react';
import { BookOpenIcon, NetworkIcon, SettingsIcon } from 'lucide-react';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { useRpc } from '@/rpc/RpcProvider';
import { describeEnvironment, ping } from '@/rpc/methods';
import { useAppStore } from '@/store/appStore';
import { useSystemTheme } from '@/theme/useSystemTheme';
import { Button } from '@/components/ui/button';
import { GitMissingBanner } from '@/components/EnvironmentBanner';
import { WelcomeScreen } from '@/components/WelcomeScreen';
import { RepositoryScreen } from '@/components/RepositoryScreen';
import { SettingsScreen } from '@/components/SettingsScreen';
import { AnalysisScreen } from '@/components/AnalysisScreen';
import { ProfileScreen } from '@/components/ProfileScreen';
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
          {screen === 'settings' || screen === 'profile' || screen === 'analysis' ? (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => showScreen(useAppStore.getState().repositoryInfo ? 'repository' : 'welcome')}
            >
              {t('app.nav.back')}
            </Button>
          ) : (
            <>
              {screen === 'repository' && (
                <>
                  <Button variant="ghost" size="sm" onClick={() => showScreen('analysis')}>
                    <NetworkIcon aria-hidden />
                    {t('app.nav.analysis')}
                  </Button>
                  <Button variant="ghost" size="sm" onClick={() => showScreen('profile')}>
                    <BookOpenIcon aria-hidden />
                    {t('app.nav.profile')}
                  </Button>
                </>
              )}
              <Button variant="ghost" size="sm" onClick={() => showScreen('settings')}>
                <SettingsIcon aria-hidden />
                {t('app.nav.settings')}
              </Button>
            </>
          )}
        </nav>
      </header>

      {/*
        Three widths, and the analysis screen is the one that changed in Iteration 8. The graph is
        a canvas: it has to be given a height and told not to scroll, because a canvas inside a
        scrolling column is a canvas the reviewer scrolls past instead of panning. So the analysis
        screen gets the window with no padding and no scrollbar of its own, and manages its own
        regions — a rail that scrolls, a diagram that does not.

        The repository screen gets the full width but keeps its padding: a changed-file list wants
        room for path, status, line counts, language and project on one row. Forms read badly
        stretched, so welcome and settings keep the narrow measure.
      */}
      <main
        className={
          screen === 'analysis'
            ? 'flex min-h-0 flex-1 flex-col overflow-hidden'
            : 'flex-1 overflow-auto px-8 py-6'
        }
      >
        {screen === 'analysis' ? (
          <AnalysisScreen />
        ) : (
          <div
            className={
              screen === 'repository'
                ? 'flex flex-col gap-6'
                : 'mx-auto flex max-w-3xl flex-col gap-6'
            }
          >
            <GitMissingBanner />

            {connection !== 'connected' && <HostPanel />}

            {screen === 'welcome' && <WelcomeScreen />}
            {screen === 'repository' && <RepositoryScreen />}
            {screen === 'settings' && <SettingsScreen />}
            {screen === 'profile' && <ProfileScreen />}
          </div>
        )}
      </main>
    </div>
  );
}
