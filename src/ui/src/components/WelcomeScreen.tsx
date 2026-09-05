import { useCallback, useState, type FormEvent } from 'react';
import { FolderOpenIcon, Loader2Icon } from 'lucide-react';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { browseForRepository, openRepository } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { RecentRepositoryList } from './RecentRepositoryList';

export function WelcomeScreen() {
  const t = useT();
  const client = useRpc();

  const [typedPath, setTypedPath] = useState('');
  const [browsing, setBrowsing] = useState(false);

  const status = useAppStore((state) => state.repository);
  const error = useAppStore((state) => state.repositoryError);
  const environment = useAppStore((state) => state.environment);
  const gitAvailable = useAppStore((state) => state.environmentInfo?.gitAvailable);
  const startOpening = useAppStore((state) => state.startOpeningRepository);
  const setRepository = useAppStore((state) => state.setRepository);
  const failRepository = useAppStore((state) => state.failRepository);

  // Without git nothing here can succeed, so the controls are disabled rather than offering
  // an action that is guaranteed to fail. They stay disabled while the probe is still running
  // too, so a missing git cannot be discovered by clicking during the gap.
  const blocked = environment === 'checking' || gitAvailable === false || !client;
  const busy = status === 'opening' || browsing;

  const open = useCallback(
    async (path: string) => {
      if (!client || !path.trim()) return;

      startOpening();
      try {
        const result = await openRepository(client, { path: path.trim() });
        setRepository(
          result.repository,
          // Only surfaced when the host actually resolved upwards, so the user is told their
          // path changed rather than discovering it later.
          result.normalizedFromSubdirectory ? path.trim() : undefined,
        );
      } catch (caught) {
        failRepository(describeError(caught));
      }
    },
    [client, startOpening, setRepository, failRepository],
  );

  const browse = useCallback(async () => {
    if (!client) return;

    setBrowsing(true);
    try {
      const result = await browseForRepository(client, { title: t('welcome.pickerTitle') });
      // Dismissing the dialog is an ordinary outcome, not a failure to report.
      if (!result.cancelled && result.path) {
        await open(result.path);
      }
    } catch (caught) {
      failRepository(describeError(caught));
    } finally {
      setBrowsing(false);
    }
  }, [client, t, open, failRepository]);

  const submit = useCallback(
    (event: FormEvent) => {
      event.preventDefault();
      void open(typedPath);
    },
    [open, typedPath],
  );

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader>
          <CardTitle>{t('welcome.heading')}</CardTitle>
          <CardDescription>{t('welcome.description')}</CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {environment === 'checking' && (
            <p className="text-muted-foreground flex items-center gap-2 text-sm" aria-live="polite">
              <Loader2Icon className="size-4 animate-spin" aria-hidden />
              {t('environment.checking')}
            </p>
          )}

          <Button onClick={() => void browse()} disabled={blocked || busy} className="w-fit">
            {browsing ? (
              <>
                <Loader2Icon className="animate-spin" aria-hidden />
                {t('welcome.browsing')}
              </>
            ) : (
              <>
                <FolderOpenIcon aria-hidden />
                {t('welcome.browse')}
              </>
            )}
          </Button>

          {/*
            A path field alongside the native picker. Pasting a path is often faster than
            navigating to it, and it is the way out if the picker misbehaves on a platform.
          */}
          <form className="flex flex-col gap-2" onSubmit={submit}>
            <Label htmlFor="repository-path">{t('welcome.pathLabel')}</Label>
            <div className="flex gap-2">
              <Input
                id="repository-path"
                value={typedPath}
                spellCheck={false}
                autoComplete="off"
                placeholder={t('welcome.pathPlaceholder')}
                disabled={blocked || busy}
                onChange={(event) => setTypedPath(event.target.value)}
              />
              <Button type="submit" variant="outline" disabled={blocked || busy || !typedPath.trim()}>
                {status === 'opening' ? t('welcome.opening') : t('welcome.open')}
              </Button>
            </div>
          </form>

          {error && (
            <p role="alert" className="text-destructive text-sm">
              {error}
            </p>
          )}
        </CardContent>
      </Card>

      <RecentRepositoryList onOpen={(path) => void open(path)} disabled={blocked || busy} />
    </div>
  );
}
