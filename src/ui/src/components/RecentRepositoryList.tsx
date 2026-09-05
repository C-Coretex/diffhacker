import { useCallback, useEffect, useState } from 'react';
import { FolderGitIcon, FolderXIcon, Loader2Icon } from 'lucide-react';
import type { RecentRepository } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { forgetRecentRepository, listRecentRepositories } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';

interface RecentRepositoryListProps {
  onOpen(path: string): void;
  disabled: boolean;
}

export function RecentRepositoryList({ onOpen, disabled }: RecentRepositoryListProps) {
  const t = useT();
  const client = useRpc();

  const status = useAppStore((state) => state.recents);
  const entries = useAppStore((state) => state.recentRepositories);
  const error = useAppStore((state) => state.recentsError);
  const startLoading = useAppStore((state) => state.startLoadingRecents);
  const setRecents = useAppStore((state) => state.setRecents);
  const failRecents = useAppStore((state) => state.failRecents);

  const refresh = useCallback(async () => {
    if (!client) return;
    startLoading();
    try {
      const result = await listRecentRepositories(client);
      setRecents([...result.entries]);
    } catch (caught) {
      failRecents(describeError(caught));
    }
  }, [client, startLoading, setRecents, failRecents]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const forget = useCallback(
    async (path: string) => {
      if (!client) return;
      try {
        await forgetRecentRepository(client, { path });
        await refresh();
      } catch (caught) {
        failRecents(describeError(caught));
      }
    },
    [client, refresh, failRecents],
  );

  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-sm font-medium">{t('welcome.recentHeading')}</h2>

      {status === 'loading' && (
        <p className="text-muted-foreground flex items-center gap-2 text-sm" aria-live="polite">
          <Loader2Icon className="size-4 animate-spin" aria-hidden />
          {t('welcome.recentLoading')}
        </p>
      )}

      {status === 'error' && (
        <p role="alert" className="text-destructive text-sm">
          {error}
        </p>
      )}

      {status === 'ready' && entries.length === 0 && (
        <p className="text-muted-foreground rounded-lg border border-dashed px-4 py-6 text-center text-sm">
          {t('welcome.recentEmpty')}
        </p>
      )}

      {status === 'ready' && entries.length > 0 && (
        <ul className="flex flex-col gap-1">
          {entries.map((entry) => (
            <RecentRow
              key={entry.path}
              entry={entry}
              disabled={disabled}
              onOpen={onOpen}
              onForget={forget}
            />
          ))}
        </ul>
      )}
    </section>
  );
}

/**
 * The host sends an ISO instant; the browser formats it in the viewer's own locale and time
 * zone. An unparseable value degrades to the raw string rather than rendering "Invalid Date".
 */
function formatLastOpened(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) {
    return iso;
  }

  return parsed.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}

interface RecentRowProps {
  entry: RecentRepository;
  disabled: boolean;
  onOpen(path: string): void;
  onForget(path: string): void;
}

function RecentRow({ entry, disabled, onOpen, onForget }: RecentRowProps) {
  const t = useT();
  const [confirming, setConfirming] = useState(false);

  return (
    <li className="hover:bg-secondary/60 flex items-center gap-3 rounded-md px-2 py-2 transition-colors">
      {entry.available ? (
        <FolderGitIcon className="text-muted-foreground size-4 shrink-0" aria-hidden />
      ) : (
        <FolderXIcon className="text-muted-foreground size-4 shrink-0" aria-hidden />
      )}

      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="truncate text-sm font-medium">{entry.name}</span>
          {/* The entry stays listed when the folder is gone, so it can be removed on purpose. */}
          {!entry.available && (
            <Badge variant="warning" title={t('welcome.unavailableHint')}>
              {t('welcome.unavailable')}
            </Badge>
          )}
        </div>
        <p className="text-muted-foreground truncate text-xs" title={entry.path}>
          {entry.path}
        </p>
        <p className="text-muted-foreground text-xs">
          {t('welcome.lastOpened', { when: formatLastOpened(entry.lastOpenedUtc) })}
        </p>
      </div>

      {entry.available && !confirming && (
        <Button size="sm" variant="ghost" disabled={disabled} onClick={() => onOpen(entry.path)}>
          {t('welcome.open')}
        </Button>
      )}

      {confirming ? (
        <div className="flex items-center gap-1">
          <Button size="sm" variant="destructive" onClick={() => onForget(entry.path)}>
            {t('welcome.forget')}
          </Button>
          <Button size="sm" variant="ghost" onClick={() => setConfirming(false)}>
            {t('providers.cancel')}
          </Button>
        </div>
      ) : (
        <Button
          size="sm"
          variant="ghost"
          aria-label={t('welcome.forgetLabel', { name: entry.name })}
          onClick={() => setConfirming(true)}
        >
          {t('welcome.forget')}
        </Button>
      )}
    </li>
  );
}
