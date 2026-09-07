import { useCallback, useEffect, useId, useState } from 'react';
import type { ProfileState } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { saveProfileNotes } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Separator } from '@/components/ui/separator';
import { Textarea } from '@/components/ui/textarea';

/**
 * Everything about a repository that the user wrote rather than a model.
 *
 * Its own card, its own save method, its own columns. Analysing the repository again replaces the
 * card above this one entirely and cannot reach anything here — which is the whole of requirement
 * 5, expressed as a shape rather than as a rule someone has to remember.
 *
 * It works before a profile has ever been generated: "ignore the generated/ folder" is worth
 * writing down on day one.
 */
export function UserSectionsForm({ state }: { state: ProfileState }) {
  const t = useT();
  const client = useRpc();
  const setProfile = useAppStore((store) => store.setProfile);

  const notesId = useId();
  const instructionsId = useId();
  const globsId = useId();
  const budgetId = useId();

  const [notes, setNotes] = useState(state.userNotes);
  const [instructions, setInstructions] = useState(state.customInstructions);
  const [globs, setGlobs] = useState(() => state.customExcludedGlobs.join('\n'));
  const [budget, setBudget] = useState(String(state.characterBudget));

  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string>();

  // Keyed on which profile this is rather than on the state object. Every save replaces the state,
  // and reseeding on that would overwrite the fields with what was just saved and clear the
  // confirmation in the same breath — so the user never saw "Saved." at all. Regenerating and
  // forgetting both move generatedAtUtc, which is when a reseed is actually wanted.
  useEffect(() => {
    setNotes(state.userNotes);
    setInstructions(state.customInstructions);
    setGlobs(state.customExcludedGlobs.join('\n'));
    setBudget(String(state.characterBudget));
    setSaved(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.repositoryPath, state.generatedAtUtc]);

  const save = useCallback(async () => {
    if (!client) return;

    setSaving(true);
    setError(undefined);

    const parsed = Number.parseInt(budget, 10);

    try {
      setProfile(
        await saveProfileNotes(client, {
          repositoryPath: state.repositoryPath,
          userNotes: notes,
          customInstructions: instructions,
          customExcludedGlobs: globs
            .split('\n')
            .map((line) => line.trim())
            .filter((line) => line.length > 0),
          ...(Number.isFinite(parsed) ? { characterBudget: parsed } : {}),
        }),
      );

      setSaved(true);
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setSaving(false);
    }
  }, [client, state.repositoryPath, notes, instructions, globs, budget, setProfile]);

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('profile.userSections')}</CardTitle>
        <CardDescription>{t('profile.userSectionsHint')}</CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-6">
        <div className="flex flex-col gap-2">
          <Label htmlFor={notesId}>{t('profile.userNotes')}</Label>
          <p className="text-muted-foreground text-xs">{t('profile.userNotesHint')}</p>
          <Textarea id={notesId} rows={6} value={notes} onChange={(event) => setNotes(event.target.value)} />
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor={instructionsId}>{t('profile.customInstructions')}</Label>
          <p className="text-muted-foreground text-xs">{t('profile.customInstructionsHint')}</p>
          <Textarea
            id={instructionsId}
            rows={4}
            value={instructions}
            onChange={(event) => setInstructions(event.target.value)}
          />
        </div>

        <Separator />

        <div className="flex flex-col gap-2">
          <Label htmlFor={globsId}>{t('profile.withheld')}</Label>
          <p className="text-muted-foreground text-xs">{t('profile.withheldHint')}</p>

          <details className="text-muted-foreground text-xs">
            <summary className="cursor-pointer">{t('profile.withheldBuiltIn')}</summary>
            <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 font-mono">
              {state.effectiveExcludedGlobs
                .filter((glob) => !state.customExcludedGlobs.includes(glob))
                .map((glob) => (
                  <li key={glob}>{glob}</li>
                ))}
            </ul>
          </details>

          <Label htmlFor={globsId} className="text-xs">
            {t('profile.withheldCustom')}
          </Label>
          <Textarea
            id={globsId}
            rows={4}
            className="font-mono"
            value={globs}
            onChange={(event) => setGlobs(event.target.value)}
          />
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor={budgetId}>{t('profile.budgetLabel')}</Label>
          <p className="text-muted-foreground text-xs">{t('profile.budgetHint')}</p>
          <Input
            id={budgetId}
            type="number"
            className="w-40"
            value={budget}
            onChange={(event) => setBudget(event.target.value)}
          />
        </div>

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
