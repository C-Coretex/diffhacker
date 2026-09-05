import { useCallback, useEffect } from 'react';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { useRpc } from '@/rpc/RpcProvider';
import { onProgress, startCountdown } from '@/rpc/methods';
import { useAppStore } from '@/store/appStore';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';

const DEMO_STEPS = 5;

/**
 * Drives the bridge in both directions: one typed request that returns a typed result, then a
 * stream of notifications pushed back by the host.
 */
export function DemoPanel() {
  const t = useT();
  const client = useRpc();

  const demo = useAppStore((state) => state.demo);
  const demoError = useAppStore((state) => state.demoError);
  const progress = useAppStore((state) => state.progress);
  const start = useAppStore((state) => state.startDemo);
  const record = useAppStore((state) => state.recordProgress);
  const fail = useAppStore((state) => state.failDemo);

  useEffect(() => {
    if (!client) {
      return;
    }

    return onProgress(client, record);
  }, [client, record]);

  const run = useCallback(async () => {
    if (!client) {
      return;
    }

    start();
    try {
      await startCountdown(client, { steps: DEMO_STEPS, delayMilliseconds: 200 });
    } catch (error) {
      fail(describeError(error));
    }
  }, [client, start, fail]);

  const latest = progress.at(-1);

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('demo.heading')}</CardTitle>
        <CardDescription>{t('demo.description')}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col items-start gap-4">
        <Button onClick={run} disabled={!client || demo === 'running'}>
          {demo === 'running' ? t('demo.running') : t('demo.run')}
        </Button>

        <div className="w-full" aria-live="polite">
          {demo === 'idle' && <p className="text-muted-foreground text-sm">{t('demo.idle')}</p>}

          {demo === 'error' && (
            <p role="alert" className="text-destructive text-sm">
              {demoError}
            </p>
          )}

          {demo === 'running' && latest && (
            <p className="text-sm">{t('demo.step', { step: latest.step + 1, total: latest.totalSteps })}</p>
          )}

          {demo === 'done' && <p className="text-sm">{t('demo.done', { count: progress.length })}</p>}

          {progress.length > 0 && (
            <ol className="text-muted-foreground mt-3 space-y-1 font-mono text-xs">
              {progress.map((notification) => (
                <li key={`${notification.operationId}-${notification.step}`}>
                  {t('demo.step', { step: notification.step + 1, total: notification.totalSteps })}
                </li>
              ))}
            </ol>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
