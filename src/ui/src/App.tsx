import { useEffect } from 'react';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { useRpc } from '@/rpc/RpcProvider';
import { ping, reportSelfTest } from '@/rpc/methods';
import { runSelfTest } from '@/selfTest/runSelfTest';
import { useAppStore } from '@/store/appStore';
import { useSystemTheme } from '@/theme/useSystemTheme';
import { DemoPanel } from '@/components/DemoPanel';
import { HostPanel } from '@/components/HostPanel';

export function App() {
  const t = useT();
  useSystemTheme();

  const client = useRpc();
  const setConnected = useAppStore((state) => state.setConnected);
  const setDetached = useAppStore((state) => state.setDetached);
  const setConnectionError = useAppStore((state) => state.setConnectionError);

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
  }, [client, setConnected, setDetached, setConnectionError]);

  return (
    <div className="flex h-screen flex-col overflow-hidden">
      <header className="shrink-0 border-b px-8 py-6">
        <h1 className="text-2xl font-semibold tracking-tight">{t('app.title')}</h1>
        <p className="text-muted-foreground mt-1 text-sm">{t('app.tagline')}</p>
      </header>

      <main className="flex-1 overflow-auto px-8 py-6">
        <div className="mx-auto flex max-w-3xl flex-col gap-6">
          <HostPanel />
          <DemoPanel />
          <p className="text-muted-foreground text-sm">{t('app.placeholder')}</p>
        </div>
      </main>
    </div>
  );
}
