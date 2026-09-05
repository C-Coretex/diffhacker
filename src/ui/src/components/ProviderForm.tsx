import { useCallback, useId, useState, type FormEvent } from 'react';
import { Loader2Icon } from 'lucide-react';
import type { ProviderProfile, SaveProviderRequestProviderType } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { saveProvider } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select } from '@/components/ui/select';

const providerTypes = [
  'openai',
  'anthropic',
  'gemini',
  'grok',
  'deepseek',
  'openai_compatible',
] as const satisfies readonly SaveProviderRequestProviderType[];

const typeLabels = {
  openai: 'providers.type.openai',
  anthropic: 'providers.type.anthropic',
  gemini: 'providers.type.gemini',
  grok: 'providers.type.grok',
  deepseek: 'providers.type.deepseek',
  openai_compatible: 'providers.type.openai_compatible',
} as const satisfies Record<SaveProviderRequestProviderType, string>;

interface ProviderFormProps {
  /** The profile being edited, or undefined when adding a new one. */
  profile?: ProviderProfile;
  onDone(): void;
}

export function ProviderForm({ profile, onDone }: ProviderFormProps) {
  const t = useT();
  const client = useRpc();
  const fieldId = useId();

  const setProviders = useAppStore((state) => state.setProviders);

  const [providerType, setProviderType] = useState<SaveProviderRequestProviderType>(
    (profile?.providerType as SaveProviderRequestProviderType | undefined) ?? 'openai',
  );
  const [displayName, setDisplayName] = useState(profile?.displayName ?? '');
  const [model, setModel] = useState(profile?.model ?? '');
  const [baseUrl, setBaseUrl] = useState(profile?.baseUrl ?? '');
  const [apiKey, setApiKey] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();

  const baseUrlRequired = providerType === 'openai_compatible';
  const suggestionsId = `${fieldId}-models`;

  const submit = useCallback(
    async (event: FormEvent) => {
      event.preventDefault();
      if (!client) return;

      setSaving(true);
      setError(undefined);
      try {
        const result = await saveProvider(client, {
          // Only present when editing, so the host knows to update rather than create.
          ...(profile ? { id: profile.id } : {}),
          providerType,
          displayName: displayName.trim() || t(typeLabels[providerType]),
          model,
          ...(baseUrl.trim() ? { baseUrl: baseUrl.trim() } : {}),
          // Omitted when blank: on an edit that means "keep the key already stored", which is
          // what lets the form work without the key ever being sent back to it.
          ...(apiKey ? { apiKey } : {}),
        });

        setProviders([...result.profiles], result.activeProfileId);
        onDone();
      } catch (caught) {
        setError(describeError(caught));
      } finally {
        setSaving(false);
      }
    },
    [client, profile, providerType, displayName, model, baseUrl, apiKey, t, setProviders, onDone],
  );

  return (
    <Card>
      <CardHeader>
        <CardTitle>{profile ? t('providers.edit') : t('providers.add')}</CardTitle>
      </CardHeader>

      <form onSubmit={submit}>
        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-col gap-2">
            <Label htmlFor={`${fieldId}-type`}>{t('providers.typeLabel')}</Label>
            <Select
              id={`${fieldId}-type`}
              value={providerType}
              onChange={(event) =>
                setProviderType(event.target.value as SaveProviderRequestProviderType)
              }
            >
              {providerTypes.map((type) => (
                <option key={type} value={type}>
                  {t(typeLabels[type])}
                </option>
              ))}
            </Select>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor={`${fieldId}-name`}>{t('providers.nameLabel')}</Label>
            <Input
              id={`${fieldId}-name`}
              value={displayName}
              autoComplete="off"
              placeholder={t('providers.namePlaceholder')}
              onChange={(event) => setDisplayName(event.target.value)}
            />
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor={`${fieldId}-model`}>{t('providers.modelLabel')}</Label>
            <Input
              id={`${fieldId}-model`}
              value={model}
              required
              spellCheck={false}
              autoComplete="off"
              list={suggestionsId}
              placeholder={t('providers.modelPlaceholder')}
              onChange={(event) => setModel(event.target.value)}
            />
            {/*
              Free text with suggestions, exactly as requirement 4 asks. The suggestions come
              from the last successful connection test — the models this key can actually
              reach — so there is no hardcoded list anywhere to go stale.
            */}
            <datalist id={suggestionsId}>
              {profile?.modelSuggestions.map((suggestion) => (
                <option key={suggestion} value={suggestion} />
              ))}
            </datalist>
            <p className="text-muted-foreground text-xs">{t('providers.modelHint')}</p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor={`${fieldId}-url`}>
              {baseUrlRequired ? t('providers.baseUrlLabel') : t('providers.baseUrlOptional')}
            </Label>
            <Input
              id={`${fieldId}-url`}
              type="url"
              value={baseUrl}
              required={baseUrlRequired}
              spellCheck={false}
              autoComplete="off"
              placeholder={t('providers.baseUrlPlaceholder')}
              onChange={(event) => setBaseUrl(event.target.value)}
            />
            <p className="text-muted-foreground text-xs">
              {baseUrlRequired ? t('providers.baseUrlRequiredHint') : t('providers.baseUrlHint')}
            </p>
          </div>

          <div className="flex flex-col gap-2">
            <Label htmlFor={`${fieldId}-key`}>{t('providers.apiKeyLabel')}</Label>
            <Input
              id={`${fieldId}-key`}
              type="password"
              value={apiKey}
              required={!profile}
              spellCheck={false}
              autoComplete="off"
              placeholder={t('providers.apiKeyPlaceholder')}
              onChange={(event) => setApiKey(event.target.value)}
            />
            {profile && <p className="text-muted-foreground text-xs">{t('providers.apiKeyUnchanged')}</p>}
          </div>

          {error && (
            <p role="alert" className="text-destructive text-sm">
              {error}
            </p>
          )}
        </CardContent>

        <CardFooter className="pt-4">
          <Button type="submit" disabled={saving || !client}>
            {saving ? (
              <>
                <Loader2Icon className="animate-spin" aria-hidden />
                {t('providers.saving')}
              </>
            ) : (
              t('providers.save')
            )}
          </Button>
          <Button type="button" variant="ghost" onClick={onDone} disabled={saving}>
            {t('providers.cancel')}
          </Button>
        </CardFooter>
      </form>
    </Card>
  );
}
