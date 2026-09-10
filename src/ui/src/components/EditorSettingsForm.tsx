import { useCallback, useEffect, useState } from 'react';
import { CheckIcon, Loader2Icon } from 'lucide-react';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { describeEditors, saveEditorSettings } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

/**
 * The external-editor settings — the configurable half of requirement 3.
 *
 * Two fields, and no editor picker. VS Code and Visual Studio are found by the host rather than
 * configured, so what is left to configure is the case they do not cover, and asking someone to pick
 * a default from a list of one is a worse interface than a button that is simply there.
 *
 * The placeholders are the whole contract of the field, so the hint says what they are rather than
 * making the reviewer guess and find out by pressing a button that does nothing useful. Neither
 * command goes through a shell — the first word is the program and the rest are separate arguments —
 * which is worth saying too, because it is the difference between quoting a path and not needing to.
 */
export function EditorSettingsForm() {
  const t = useT();
  const client = useRpc();

  const editors = useAppStore((state) => state.editors);
  const setEditors = useAppStore((state) => state.setEditors);

  const [diff, setDiff] = useState('');
  const [open, setOpen] = useState('');
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string>();

  useEffect(() => {
    if (!client) return;

    describeEditors(client)
      .then((described) => {
        setEditors(described);
        setDiff(described.customDiffCommand);
        setOpen(described.customOpenCommand);
      })
      .catch((caught: unknown) => setError(describeError(caught)));
  }, [client, setEditors]);

  useEffect(() => {
    if (!saved) return;

    const timer = setTimeout(() => setSaved(false), 2000);
    return () => clearTimeout(timer);
  }, [saved]);

  const save = useCallback(async () => {
    if (!client) return;

    setSaving(true);
    setError(undefined);

    try {
      const result = await saveEditorSettings(client, {
        customDiffCommand: diff,
        customOpenCommand: open,
      });

      setEditors(result);
      setDiff(result.customDiffCommand);
      setOpen(result.customOpenCommand);
      setSaved(true);
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setSaving(false);
    }
  }, [client, diff, open, setEditors]);

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('editors.heading')}</CardTitle>
        <CardDescription>{t('editors.description')}</CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-4">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          {editors?.vsCodeAvailable || editors?.visualStudioAvailable ? (
            <>
              <span className="text-muted-foreground">{t('editors.detected')}</span>
              {editors.vsCodeAvailable && <Badge variant="secondary">{t('editors.vsCode')}</Badge>}
              {editors.visualStudioAvailable && (
                <Badge variant="secondary">{t('editors.visualStudio')}</Badge>
              )}
            </>
          ) : (
            <span className="text-muted-foreground">{t('editors.detectedNone')}</span>
          )}
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="editor-diff-command">{t('editors.diffLabel')}</Label>
          <Input
            id="editor-diff-command"
            value={diff}
            spellCheck={false}
            placeholder={t('editors.diffPlaceholder')}
            onChange={(event) => setDiff(event.currentTarget.value)}
            className="font-mono text-xs"
          />
          <p className="text-xs text-muted-foreground">{t('editors.diffHint')}</p>
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="editor-open-command">{t('editors.openLabel')}</Label>
          <Input
            id="editor-open-command"
            value={open}
            spellCheck={false}
            placeholder={t('editors.openPlaceholder')}
            onChange={(event) => setOpen(event.currentTarget.value)}
            className="font-mono text-xs"
          />
          <p className="text-xs text-muted-foreground">{t('editors.openHint')}</p>
        </div>

        {error && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}

        <div className="flex items-center gap-3">
          <Button className="w-fit" disabled={saving || !client} onClick={() => void save()}>
            {saving && <Loader2Icon className="size-4 animate-spin" aria-hidden />}
            {t(saving ? 'editors.saving' : 'editors.save')}
          </Button>

          {saved && (
            <span className="flex items-center gap-1 text-sm text-muted-foreground" aria-live="polite">
              <CheckIcon className="size-4" aria-hidden />
              {t('editors.saved')}
            </span>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
