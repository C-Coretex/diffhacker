import { useCallback, useEffect, useId, useState } from 'react';
import { PlusIcon } from 'lucide-react';
import type { ProfileState, SaveProfileRequest } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { formatCount } from '@/i18n/format';
import { useT } from '@/i18n/useT';
import { saveProfileDocument } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Separator } from '@/components/ui/separator';
import { Textarea } from '@/components/ui/textarea';

interface ModuleDraft {
  name: string;
  path: string;
  summary: string;
  relatedModules: string;
}

interface EntryPointDraft {
  path: string;
  purpose: string;
}

/**
 * The generated half of the profile, editable.
 *
 * Everything in here is replaced the next time the repository is analysed, and the form says so
 * before the user types into it. The parts that survive a rerun live in a different card, saved
 * through a different method, written to different columns — the separation is structural rather
 * than a promise this component makes.
 */
export function ProfileDocumentForm({ state }: { state: ProfileState }) {
  const t = useT();
  const client = useRpc();
  const setProfile = useAppStore((store) => store.setProfile);

  const [purpose, setPurpose] = useState(state.purpose ?? '');
  const [architecture, setArchitecture] = useState(state.architecture ?? '');
  const [layering, setLayering] = useState(state.layering ?? '');
  const [patterns, setPatterns] = useState(state.patterns ?? '');
  const [testLayout, setTestLayout] = useState(state.testLayout ?? '');
  const [modules, setModules] = useState<ModuleDraft[]>(() => toModuleDrafts(state));
  const [entryPoints, setEntryPoints] = useState<EntryPointDraft[]>(() => toEntryPointDrafts(state));

  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string>();

  // A rerun replaces every field here. Reseeding from the new state is the correct behaviour and
  // the reason the warning above the form exists — but it is keyed on which run produced the
  // document, not on the state object: saving also replaces the state, and reseeding on that would
  // clear the confirmation in the same render that set it.
  useEffect(() => {
    setPurpose(state.purpose ?? '');
    setArchitecture(state.architecture ?? '');
    setLayering(state.layering ?? '');
    setPatterns(state.patterns ?? '');
    setTestLayout(state.testLayout ?? '');
    setModules(toModuleDrafts(state));
    setEntryPoints(toEntryPointDrafts(state));
    setSaved(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.repositoryPath, state.generatedAtUtc]);

  const save = useCallback(async () => {
    if (!client) return;

    setSaving(true);
    setError(undefined);

    const request: SaveProfileRequest = {
      repositoryPath: state.repositoryPath,
      purpose,
      architecture,
      layering,
      patterns,
      testLayout,
      documentationSources: state.documentationSources,
      modules: modules.map((module) => ({
        name: module.name,
        path: module.path,
        summary: module.summary,
        relatedModules: splitList(module.relatedModules),
      })),
      entryPoints: entryPoints.map((point) => ({ path: point.path, purpose: point.purpose })),
    };

    try {
      setProfile(await saveProfileDocument(client, request));
      setSaved(true);
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setSaving(false);
    }
  }, [
    client,
    state.repositoryPath,
    state.documentationSources,
    purpose,
    architecture,
    layering,
    patterns,
    testLayout,
    modules,
    entryPoints,
    setProfile,
  ]);

  const overBudget = (state.characterCount ?? 0) > state.characterBudget;

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('profile.generatedSections')}</CardTitle>
        <CardDescription>{t('profile.generatedWarning')}</CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-6">
        <p className={overBudget ? 'text-destructive text-xs' : 'text-muted-foreground text-xs'}>
          {t('profile.budget', {
            count: formatCount(state.characterCount ?? 0),
            budget: formatCount(state.characterBudget),
          })}
        </p>

        {overBudget && (
          <Alert variant="warning">
            <AlertDescription>
              {t('profile.budgetExceeded', { budget: formatCount(state.characterBudget) })}
            </AlertDescription>
          </Alert>
        )}

        <Field label={t('profile.purpose')} value={purpose} onChange={setPurpose} rows={3} />
        <Field label={t('profile.architecture')} value={architecture} onChange={setArchitecture} rows={8} />

        <div className="flex flex-col gap-3">
          <h3 className="text-sm font-medium">{t('profile.modules')}</h3>

          {modules.map((module, index) => (
            <ModuleRow
              // Index as the key: these rows have no identity of their own, and a name the user is
              // still typing would remount the field on every keystroke.
              key={index}
              module={module}
              onChange={(next) =>
                setModules((current) => current.map((item, i) => (i === index ? next : item)))
              }
              onRemove={() => setModules((current) => current.filter((_, i) => i !== index))}
            />
          ))}

          <Button
            type="button"
            size="sm"
            variant="outline"
            className="w-fit"
            onClick={() =>
              setModules((current) => [...current, { name: '', path: '', summary: '', relatedModules: '' }])
            }
          >
            <PlusIcon aria-hidden />
            {t('profile.addModule')}
          </Button>
        </div>

        <Field label={t('profile.layering')} value={layering} onChange={setLayering} rows={5} />
        <Field label={t('profile.patterns')} value={patterns} onChange={setPatterns} rows={6} />

        <div className="flex flex-col gap-3">
          <h3 className="text-sm font-medium">{t('profile.entryPoints')}</h3>

          {entryPoints.map((point, index) => (
            <EntryPointRow
              key={index}
              entryPoint={point}
              onChange={(next) =>
                setEntryPoints((current) => current.map((item, i) => (i === index ? next : item)))
              }
              onRemove={() => setEntryPoints((current) => current.filter((_, i) => i !== index))}
            />
          ))}

          <Button
            type="button"
            size="sm"
            variant="outline"
            className="w-fit"
            onClick={() => setEntryPoints((current) => [...current, { path: '', purpose: '' }])}
          >
            <PlusIcon aria-hidden />
            {t('profile.addEntryPoint')}
          </Button>
        </div>

        <Field label={t('profile.testLayout')} value={testLayout} onChange={setTestLayout} rows={5} />

        <Separator />

        <p className="text-muted-foreground text-xs">
          {state.documentationSources.length > 0
            ? t('profile.documentationRead', { files: state.documentationSources.join(', ') })
            : t('profile.noDocumentationRead')}
        </p>

        {error && (
          <p role="alert" className="text-destructive text-sm">
            {error}
          </p>
        )}

        <div className="flex items-center gap-3">
          <Button type="button" className="w-fit" disabled={saving} onClick={() => void save()}>
            {saving ? t('profile.saving') : t('profile.save')}
          </Button>
          {saved && <span className="text-muted-foreground text-xs">{t('profile.saved')}</span>}
        </div>
      </CardContent>
    </Card>
  );
}

interface FieldProps {
  label: string;
  value: string;
  rows: number;
  onChange(value: string): void;
}

function Field({ label, value, rows, onChange }: FieldProps) {
  const id = useId();

  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label}</Label>
      <Textarea id={id} rows={rows} value={value} onChange={(event) => onChange(event.target.value)} />
    </div>
  );
}

interface ModuleRowProps {
  module: ModuleDraft;
  onChange(module: ModuleDraft): void;
  onRemove(): void;
}

function ModuleRow({ module, onChange, onRemove }: ModuleRowProps) {
  const t = useT();
  const nameId = useId();
  const pathId = useId();
  const summaryId = useId();
  const relatedId = useId();

  return (
    <div className="flex flex-col gap-3 rounded-lg border p-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="flex flex-col gap-1">
          <Label htmlFor={nameId} className="text-xs">
            {t('profile.moduleName')}
          </Label>
          <Input
            id={nameId}
            value={module.name}
            onChange={(event) => onChange({ ...module, name: event.target.value })}
          />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor={pathId} className="text-xs">
            {t('profile.modulePath')}
          </Label>
          <Input
            id={pathId}
            className="font-mono"
            value={module.path}
            onChange={(event) => onChange({ ...module, path: event.target.value })}
          />
        </div>
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor={summaryId} className="text-xs">
          {t('profile.moduleSummary')}
        </Label>
        <Textarea
          id={summaryId}
          rows={2}
          value={module.summary}
          onChange={(event) => onChange({ ...module, summary: event.target.value })}
        />
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor={relatedId} className="text-xs">
          {t('profile.moduleRelated')}
        </Label>
        <Input
          id={relatedId}
          placeholder={t('profile.moduleRelatedHint')}
          value={module.relatedModules}
          onChange={(event) => onChange({ ...module, relatedModules: event.target.value })}
        />
      </div>

      <Button type="button" size="sm" variant="ghost" className="w-fit" onClick={onRemove}>
        {t('profile.removeModule')}
      </Button>
    </div>
  );
}

interface EntryPointRowProps {
  entryPoint: EntryPointDraft;
  onChange(entryPoint: EntryPointDraft): void;
  onRemove(): void;
}

function EntryPointRow({ entryPoint, onChange, onRemove }: EntryPointRowProps) {
  const t = useT();
  const pathId = useId();
  const purposeId = useId();

  return (
    <div className="flex flex-col gap-3 rounded-lg border p-3 sm:flex-row sm:items-end">
      <div className="flex flex-1 flex-col gap-1">
        <Label htmlFor={pathId} className="text-xs">
          {t('profile.entryPointPath')}
        </Label>
        <Input
          id={pathId}
          className="font-mono"
          value={entryPoint.path}
          onChange={(event) => onChange({ ...entryPoint, path: event.target.value })}
        />
      </div>
      <div className="flex flex-1 flex-col gap-1">
        <Label htmlFor={purposeId} className="text-xs">
          {t('profile.entryPointPurpose')}
        </Label>
        <Input
          id={purposeId}
          value={entryPoint.purpose}
          onChange={(event) => onChange({ ...entryPoint, purpose: event.target.value })}
        />
      </div>
      <Button type="button" size="sm" variant="ghost" onClick={onRemove}>
        {t('profile.removeEntryPoint')}
      </Button>
    </div>
  );
}

function toModuleDrafts(state: ProfileState): ModuleDraft[] {
  return state.modules.map((module) => ({
    name: module.name,
    path: module.path,
    summary: module.summary,
    relatedModules: module.relatedModules.join(', '),
  }));
}

function toEntryPointDrafts(state: ProfileState): EntryPointDraft[] {
  return state.entryPoints.map((point) => ({ path: point.path, purpose: point.purpose }));
}

function splitList(value: string): string[] {
  return value
    .split(',')
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}
