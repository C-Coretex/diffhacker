import { useCallback, useEffect, useState } from 'react';
import { KeyRoundIcon, Loader2Icon, PlusIcon } from 'lucide-react';
import type { ProviderProfile, ProviderProfileProviderType } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { deleteProvider, listProviders, setActiveProvider } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Separator } from '@/components/ui/separator';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog';
import { ProviderForm } from './ProviderForm';
import { TestConnectionPanel } from './TestConnectionPanel';
import { SecretBackendNotice } from './EnvironmentBanner';

const typeLabels = {
  openai: 'providers.type.openai',
  anthropic: 'providers.type.anthropic',
  gemini: 'providers.type.gemini',
  grok: 'providers.type.grok',
  deepseek: 'providers.type.deepseek',
  openai_compatible: 'providers.type.openai_compatible',
} as const satisfies Record<ProviderProfileProviderType, string>;

/** `undefined` means no form is open; `null` means the form is open for a new profile. */
type Editing = ProviderProfile | null | undefined;

export function SettingsScreen() {
  const t = useT();
  const client = useRpc();

  const status = useAppStore((state) => state.providers);
  const profiles = useAppStore((state) => state.providerProfiles);
  const error = useAppStore((state) => state.providersError);
  const startLoading = useAppStore((state) => state.startLoadingProviders);
  const setProviders = useAppStore((state) => state.setProviders);
  const failProviders = useAppStore((state) => state.failProviders);

  const [editing, setEditing] = useState<Editing>(undefined);

  const refresh = useCallback(async () => {
    if (!client) return;
    startLoading();
    try {
      const result = await listProviders(client);
      setProviders([...result.profiles], result.activeProfileId);
    } catch (caught) {
      failProviders(describeError(caught));
    }
  }, [client, startLoading, setProviders, failProviders]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const remove = useCallback(
    async (id: string) => {
      if (!client) return;
      try {
        const result = await deleteProvider(client, { id });
        setProviders([...result.profiles], result.activeProfileId);
      } catch (caught) {
        failProviders(describeError(caught));
      }
    },
    [client, setProviders, failProviders],
  );

  const activate = useCallback(
    async (id: string) => {
      if (!client) return;
      try {
        const result = await setActiveProvider(client, { id });
        setProviders([...result.profiles], result.activeProfileId);
      } catch (caught) {
        failProviders(describeError(caught));
      }
    },
    [client, setProviders, failProviders],
  );

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader>
          <CardTitle>{t('providers.heading')}</CardTitle>
          <CardDescription>{t('providers.description')}</CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          <SecretBackendNotice />

          {status === 'loading' && (
            <p className="text-muted-foreground flex items-center gap-2 text-sm" aria-live="polite">
              <Loader2Icon className="size-4 animate-spin" aria-hidden />
              {t('providers.loading')}
            </p>
          )}

          {status === 'error' && (
            <p role="alert" className="text-destructive text-sm">
              {error}
            </p>
          )}

          {status === 'ready' && profiles.length === 0 && editing === undefined && (
            <p className="text-muted-foreground rounded-lg border border-dashed px-4 py-6 text-center text-sm">
              {t('providers.empty')}
            </p>
          )}

          {status === 'ready' && profiles.length > 0 && (
            <ul className="flex flex-col gap-3">
              {profiles.map((profile) => (
                <li key={profile.id}>
                  <ProviderRow
                    profile={profile}
                    onEdit={() => setEditing(profile)}
                    onRemove={() => void remove(profile.id)}
                    onActivate={() => void activate(profile.id)}
                  />
                </li>
              ))}
            </ul>
          )}

          {editing === undefined && (
            <Button variant="outline" className="w-fit" onClick={() => setEditing(null)}>
              <PlusIcon aria-hidden />
              {t('providers.add')}
            </Button>
          )}
        </CardContent>
      </Card>

      {editing !== undefined && (
        <ProviderForm
          {...(editing ? { profile: editing } : {})}
          onDone={() => setEditing(undefined)}
        />
      )}
    </div>
  );
}

interface ProviderRowProps {
  profile: ProviderProfile;
  onEdit(): void;
  onRemove(): void;
  onActivate(): void;
}

function ProviderRow({ profile, onEdit, onRemove, onActivate }: ProviderRowProps) {
  const t = useT();

  return (
    <div className="flex flex-col gap-3 rounded-lg border p-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-medium">{profile.displayName}</span>
        <Badge variant="secondary">{t(typeLabels[profile.providerType])}</Badge>
        {profile.isActive && <Badge title={t('providers.activeHint')}>{t('providers.active')}</Badge>}
        {!profile.hasApiKey && <Badge variant="warning">{t('providers.noKey')}</Badge>}
      </div>

      <dl className="text-muted-foreground grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
        <dt>{t('providers.modelLabel')}</dt>
        <dd className="font-mono break-all">{profile.model}</dd>
        {profile.baseUrl && (
          <>
            <dt>{t('providers.baseUrlLabel')}</dt>
            <dd className="font-mono break-all">{profile.baseUrl}</dd>
          </>
        )}
        <dt>
          <KeyRoundIcon className="size-3" aria-hidden />
        </dt>
        <dd>{profile.hasApiKey ? t('providers.keyStored') : t('providers.noKey')}</dd>
      </dl>

      <Separator />

      <TestConnectionPanel profile={profile} />

      <div className="flex flex-wrap gap-2">
        {!profile.isActive && (
          <Button size="sm" variant="secondary" onClick={onActivate}>
            {t('providers.makeActive')}
          </Button>
        )}
        <Button size="sm" variant="ghost" onClick={onEdit}>
          {t('providers.edit')}
        </Button>

        <AlertDialog>
          <AlertDialogTrigger asChild>
            <Button size="sm" variant="ghost">
              {t('providers.remove')}
            </Button>
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{t('providers.removeConfirmTitle')}</AlertDialogTitle>
              <AlertDialogDescription>
                {t('providers.removeConfirm', { name: profile.displayName })}
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>{t('providers.cancel')}</AlertDialogCancel>
              <AlertDialogAction onClick={onRemove}>{t('providers.remove')}</AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </div>
    </div>
  );
}
