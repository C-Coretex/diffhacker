import { useCallback, useEffect, useRef, useState } from 'react';
import { Loader2Icon, SparklesIcon } from 'lucide-react';
import type { ProfileState } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { formatCount } from '@/i18n/format';
import { useT } from '@/i18n/useT';
import {
  deleteProfile,
  generateProfile,
  getProfile,
  onAnalysisProgress,
  onBudgetLimitReached,
  onToolCallEvent,
} from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
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
import { BudgetPromptDialog } from './BudgetPromptDialog';
import { DocumentationPanel } from './DocumentationPanel';
import { ProfileDocumentForm } from './ProfileDocumentForm';
import { ProfileRunPanel } from './ProfileRunPanel';
import { UserSectionsForm } from './UserSectionsForm';

/**
 * The repository knowledge base.
 *
 * Four things stacked, in the order they matter: whether a profile exists and whether it is still
 * accurate; what the model found, editable; what the user has written, which no rerun touches; and
 * the documentation that can be derived from all of it.
 */
export function ProfileScreen() {
  const t = useT();
  const client = useRpc();

  const repository = useAppStore((state) => state.repositoryInfo);
  const status = useAppStore((state) => state.profile);
  const state = useAppStore((state) => state.profileState);
  const error = useAppStore((state) => state.profileError);
  const run = useAppStore((state) => state.profileRun);
  const budgetPrompt = useAppStore((state) => state.profileBudgetPrompt);

  const startLoading = useAppStore((store) => store.startLoadingProfile);
  const setProfile = useAppStore((store) => store.setProfile);
  const failProfile = useAppStore((store) => store.failProfile);
  const startRun = useAppStore((store) => store.startProfileRun);
  const endRun = useAppStore((store) => store.endProfileRun);
  const recordProgress = useAppStore((store) => store.recordProfileProgress);
  const recordEvent = useAppStore((store) => store.recordProfileRunEvent);
  const setBudgetPrompt = useAppStore((store) => store.setProfileBudgetPrompt);

  const [runError, setRunError] = useState<string>();
  const abort = useRef<AbortController>(null);

  const path = repository?.path;

  const refresh = useCallback(async () => {
    if (!client || !path) return;

    startLoading();

    try {
      setProfile(await getProfile(client, { repositoryPath: path }));
    } catch (caught) {
      failProfile(describeError(caught));
    }
  }, [client, path, startLoading, setProfile, failProfile]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Subscribed for the life of the screen rather than for the life of a run: a notification that
  // arrives a moment after the call resolves still belongs to the log.
  useEffect(() => {
    if (!client) return;

    const stopProgress = onAnalysisProgress(client, recordProgress);
    const stopEvents = onToolCallEvent(client, recordEvent);
    const stopBudgetPrompts = onBudgetLimitReached(client, setBudgetPrompt);

    return () => {
      stopProgress();
      stopEvents();
      stopBudgetPrompts();
    };
  }, [client, recordProgress, recordEvent, setBudgetPrompt]);

  const generate = useCallback(async () => {
    if (!client || !path) return;

    const controller = new AbortController();
    abort.current = controller;

    setRunError(undefined);
    startRun();

    try {
      setProfile(await generateProfile(client, { repositoryPath: path }, controller.signal));
    } catch (caught) {
      // A cancelled run is not a failure to report as one, but it did spend money, so the state is
      // reloaded rather than left showing whatever was on screen when the user pressed stop.
      setRunError(controller.signal.aborted ? t('profile.cancelled') : describeError(caught));
      void refresh();
    } finally {
      abort.current = null;
      endRun();
    }
  }, [client, path, startRun, setProfile, endRun, refresh, t]);

  const forget = useCallback(async () => {
    if (!client || !path) return;

    try {
      setProfile(await deleteProfile(client, { repositoryPath: path }));
    } catch (caught) {
      failProfile(describeError(caught));
    }
  }, [client, path, setProfile, failProfile]);

  if (!repository || !path) {
    return null;
  }

  return (
    <div className="flex flex-col gap-6">
      <BudgetPromptDialog prompt={budgetPrompt} onResolved={() => setBudgetPrompt(undefined)} />

      <Card>
        <CardHeader>
          <CardTitle>{t('profile.heading')}</CardTitle>
          <CardDescription>{t('profile.description')}</CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {status === 'loading' && (
            <p className="text-muted-foreground flex items-center gap-2 text-sm" aria-live="polite">
              <Loader2Icon className="size-4 animate-spin" aria-hidden />
              {t('profile.generating')}
            </p>
          )}

          {status === 'error' && (
            <p role="alert" className="text-destructive text-sm">
              {error}
            </p>
          )}

          {state && <Provenance state={state} />}

          {runError && (
            <p role="alert" className="text-destructive text-sm">
              {runError}
            </p>
          )}

          <div className="flex flex-wrap gap-2">
            <Button disabled={run === 'running'} onClick={() => void generate()}>
              <SparklesIcon aria-hidden />
              {run === 'running'
                ? t('profile.generating')
                : state?.hasProfile
                  ? t('profile.regenerate')
                  : t('profile.generate')}
            </Button>

            {state?.hasProfile && (
              <AlertDialog>
                <AlertDialogTrigger asChild>
                  <Button variant="ghost">{t('profile.delete')}</Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>{t('profile.deleteConfirmTitle')}</AlertDialogTitle>
                    <AlertDialogDescription>{t('profile.deleteConfirmBody')}</AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>{t('profile.keep')}</AlertDialogCancel>
                    <AlertDialogAction onClick={() => void forget()}>
                      {t('profile.deleteConfirm')}
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            )}
          </div>
        </CardContent>
      </Card>

      {run === 'running' && <ProfileRunPanel onCancel={() => abort.current?.abort()} />}

      {state?.hasProfile && <ProfileDocumentForm state={state} />}
      {state && <UserSectionsForm state={state} />}
      {state?.hasProfile && <DocumentationPanel repositoryPath={path} />}
    </div>
  );
}

/** Where the profile came from, and whether the repository has moved on since. */
function Provenance({ state }: { state: ProfileState }) {
  const t = useT();

  if (!state.hasProfile) {
    return (
      <Alert>
        <AlertTitle>{t('profile.missingHeading')}</AlertTitle>
        <AlertDescription>{t('profile.missingBody')}</AlertDescription>
      </Alert>
    );
  }

  const date = state.generatedAtUtc ? new Date(state.generatedAtUtc).toLocaleDateString() : '';

  return (
    <div className="flex flex-col gap-3">
      <p className="text-muted-foreground text-xs">
        {state.generatedFromCommit
          ? t('profile.generatedFrom', {
              date,
              commit: state.generatedFromCommit.slice(0, 8),
              model: state.generatedByModel ?? '',
            })
          : t('profile.generatedFromNoCommit', { date, model: state.generatedByModel ?? '' })}
      </p>

      {state.userSectionCharacters !== undefined && state.userSectionCharacters > 0 && (
        <p className="text-muted-foreground text-xs">
          {t('profile.userSectionSize', { count: formatCount(state.userSectionCharacters) })}
        </p>
      )}

      {state.driftSubstantial && (
        <Alert variant="warning">
          <AlertTitle>{t('profile.driftHeading')}</AlertTitle>
          <AlertDescription>
            {state.driftCommitReachable === false
              ? t('profile.driftUnreachable')
              : t('profile.driftBody', {
                  changed: formatCount(state.driftFilesChanged ?? 0),
                  tracked: formatCount(state.driftTrackedFiles ?? 0),
                })}
          </AlertDescription>
        </Alert>
      )}
    </div>
  );
}

/**
 * The banner the repository screen shows when nothing is stored yet. Requirement 10: an analysis
 * can proceed without a profile, and the interface says plainly that it will be worse.
 */
export function MissingProfileNotice({ onOpen }: { onOpen(): void }) {
  const t = useT();
  const status = useAppStore((state) => state.profile);
  const state = useAppStore((state) => state.profileState);

  if (status !== 'ready' || state?.hasProfile) {
    return null;
  }

  return (
    <Alert variant="warning">
      <AlertTitle>{t('profile.missingHeading')}</AlertTitle>
      <AlertDescription className="flex flex-col items-start gap-2">
        {t('profile.missingBody')}
        <Button size="sm" variant="outline" onClick={onOpen}>
          {t('profile.generate')}
        </Button>
      </AlertDescription>
    </Alert>
  );
}
