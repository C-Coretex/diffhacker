import { useCallback, useEffect, useState } from 'react';
import { CheckIcon, Loader2Icon } from 'lucide-react';
import type { AnalysisOptions } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { FALLBACK_OPTIONS } from '@/lib/analysisParts';
import { getAnalysisDefaults, saveAnalysisDefaults } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { AnalysisPartsFields } from '@/components/analysis/AnalysisPartsFields';

/**
 * The defaults for what an analysis asks the model for.
 *
 * The only place they change. The run options on the analysis screen start from these and are
 * forgotten after the run, so a reviewer who trims one expensive re-run has not trimmed every run
 * after it — and one who wants every run trimmed says so here, once.
 */
export function AnalysisDefaultsForm() {
  const t = useT();
  const client = useRpc();

  const setDefaults = useAppStore((state) => state.setAnalysisDefaults);

  const [value, setValue] = useState<AnalysisOptions>();
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string>();

  useEffect(() => {
    if (!client) return;

    getAnalysisDefaults(client)
      .then((defaults) => {
        setDefaults(defaults);
        setValue(defaults);
      })
      .catch((caught: unknown) => setError(describeError(caught)));
  }, [client, setDefaults]);

  useEffect(() => {
    if (!saved) return;

    const timer = setTimeout(() => setSaved(false), 2000);
    return () => clearTimeout(timer);
  }, [saved]);

  const save = useCallback(async () => {
    if (!client || !value) return;

    setSaving(true);
    setError(undefined);

    try {
      const result = await saveAnalysisDefaults(client, value);

      setDefaults(result);
      setValue(result);
      setSaved(true);
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setSaving(false);
    }
  }, [client, value, setDefaults]);

  return (
    <Card data-testid="analysis-defaults">
      <CardHeader>
        <CardTitle>{t('analysis.parts.heading')}</CardTitle>
        <CardDescription>{t('analysis.parts.description')}</CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-4">
        <AnalysisPartsFields
          value={value ?? FALLBACK_OPTIONS}
          onChange={(next) => {
            setValue(next);
            setSaved(false);
          }}
          disabled={!value || saving}
          testIdPrefix="default-option"
        />

        {error && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}

        <div className="flex items-center gap-3">
          <Button
            className="w-fit"
            disabled={saving || !client || !value}
            onClick={() => void save()}
            data-testid="analysis-defaults-save"
          >
            {saving && <Loader2Icon className="size-4 animate-spin" aria-hidden />}
            {t(saving ? 'analysis.parts.saving' : 'analysis.parts.save')}
          </Button>

          {saved && (
            <span className="flex items-center gap-1 text-sm text-muted-foreground" aria-live="polite">
              <CheckIcon className="size-4" aria-hidden />
              {t('analysis.parts.saved')}
            </span>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
