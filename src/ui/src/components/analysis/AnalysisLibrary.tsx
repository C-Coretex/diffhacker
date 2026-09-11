import { useState } from 'react';
import * as Popover from '@radix-ui/react-popover';
import { HistoryIcon, Trash2Icon } from 'lucide-react';
import type { AnalysisLibraryEntry } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { formatCount } from '@/i18n/format';
import { useT } from '@/i18n/useT';
import { deleteAnalysis, getAnalysis } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';

/**
 * Requirement 8: every earlier run of this repository, with what a reviewer chooses between —
 * when, which model, what it cost, how large the change was — and a way back into any of them.
 *
 * A popover off the header rather than a screen of its own: there are at most twenty, and choosing
 * one is a moment's decision taken while looking at the analysis it will replace. Opening one is
 * `analysis.get` with its id, which reads the stored document and starts no conversation — the
 * reason it can be instant.
 *
 * The list itself is loaded by the screen, not here, because the button's count is on screen
 * before anyone opens the popover.
 */
export function AnalysisLibraryButton({ disabled }: { disabled: boolean }) {
  const t = useT();
  const client = useRpc();

  const library = useAppStore((state) => state.analysisLibrary);
  const view = useAppStore((state) => state.analysisView);
  const setAnalysis = useAppStore((state) => state.setAnalysis);
  const setLibrary = useAppStore((state) => state.setAnalysisLibrary);

  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [pendingDelete, setPendingDelete] = useState<AnalysisLibraryEntry>();

  if (!library || library.entries.length === 0) return null;

  const reopen = async (analysisId: string) => {
    if (!client || busy) return;

    setBusy(true);
    setError(undefined);

    try {
      setAnalysis(await getAnalysis(client, { repositoryPath: library.repositoryPath, analysisId }));
      setOpen(false);
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  };

  const remove = async (entry: AnalysisLibraryEntry) => {
    if (!client) return;

    setError(undefined);

    try {
      const next = await deleteAnalysis(client, {
        repositoryPath: library.repositoryPath,
        analysisId: entry.analysisId,
      });

      setLibrary(next);

      // Deleting the run on screen leaves nothing behind it to show, so the screen moves to what
      // is left — the latest run, or the empty state — rather than drawing a deleted analysis.
      if (entry.analysisId === view?.analysisId) {
        setAnalysis(await getAnalysis(client, { repositoryPath: library.repositoryPath }));
      }
    } catch (caught) {
      setError(describeError(caught));
      setOpen(true);
    }
  };

  return (
    <>
      <Popover.Root open={open} onOpenChange={setOpen}>
        <Popover.Trigger asChild>
          {/*
            An icon and a number, with the words as its name. The header already carries the
            provenance line, both run switches and the button that spends the money; one more
            labelled button wraps it onto a second row at a common window width, and every pixel
            of that row comes out of the diagram.
          */}
          <Button
            size="sm"
            variant="outline"
            disabled={disabled}
            aria-label={t('analysis.library.button', { count: library.entries.length })}
            title={t('analysis.library.button', { count: library.entries.length })}
            data-testid="analysis-history-button"
          >
            <HistoryIcon aria-hidden />
            <span className="tabular-nums">{library.entries.length}</span>
          </Button>
        </Popover.Trigger>

        <Popover.Portal>
          <Popover.Content
            align="end"
            sideOffset={6}
            data-testid="analysis-history"
            className="z-50 flex w-[30rem] max-w-[90vw] flex-col gap-3 rounded-md border border-border bg-popover p-3 text-popover-foreground shadow-md"
          >
            <div>
              <h2 className="text-sm font-semibold">{t('analysis.library.heading')}</h2>
              <p className="text-xs text-muted-foreground">{t('analysis.library.body')}</p>
            </div>

            {error && (
              <p role="alert" className="text-xs text-destructive">
                {error}
              </p>
            )}

            <ul className="flex max-h-96 flex-col gap-1 overflow-y-auto">
              {library.entries.map((entry) => (
                <Entry
                  key={entry.analysisId}
                  entry={entry}
                  current={entry.analysisId === view?.analysisId}
                  busy={busy}
                  onOpen={() => void reopen(entry.analysisId)}
                  onDelete={() => {
                    // Closed first: a confirmation opened over a popover fights it for focus, and
                    // the popover would dismiss itself on the first click inside the dialog.
                    setOpen(false);
                    setPendingDelete(entry);
                  }}
                />
              ))}
            </ul>

            <p className="text-xs text-muted-foreground">
              {t('analysis.library.retention', { limit: library.retentionLimit })}
            </p>

            <Popover.Arrow className="fill-popover" />
          </Popover.Content>
        </Popover.Portal>
      </Popover.Root>

      <AlertDialog
        open={pendingDelete !== undefined}
        onOpenChange={(next) => !next && setPendingDelete(undefined)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('analysis.library.deleteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>
              {pendingDelete &&
                t('analysis.library.deleteBody', {
                  date: formatDate(pendingDelete.createdAtUtc),
                  model: pendingDelete.model,
                })}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('analysis.library.deleteCancel')}</AlertDialogCancel>
            <AlertDialogAction
              data-testid="analysis-history-confirm-delete"
              onClick={() => {
                if (pendingDelete) void remove(pendingDelete);
                setPendingDelete(undefined);
              }}
            >
              {t('analysis.library.deleteConfirm')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}

function Entry({
  entry,
  current,
  busy,
  onOpen,
  onDelete,
}: {
  entry: AnalysisLibraryEntry;
  current: boolean;
  busy: boolean;
  onOpen(): void;
  onDelete(): void;
}) {
  const t = useT();
  const date = formatDate(entry.createdAtUtc);

  return (
    <li
      data-testid="analysis-history-entry"
      data-analysis-id={entry.analysisId}
      data-current={current ? 'true' : 'false'}
      className={
        current
          ? 'flex items-start gap-3 rounded-md border border-primary/40 bg-primary/5 px-2.5 py-2'
          : 'flex items-start gap-3 rounded-md border border-transparent px-2.5 py-2 hover:bg-accent'
      }
    >
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="font-medium">{date}</span>
          {entry.isLatest && <Badge variant="secondary">{t('analysis.library.latest')}</Badge>}
          {current && <Badge>{t('analysis.library.current')}</Badge>}
        </div>

        <p className="truncate text-xs text-muted-foreground" data-testid="analysis-history-model">
          {entry.providerDisplayName} · {entry.model}
        </p>

        <p className="flex flex-wrap gap-x-3 text-xs text-muted-foreground tabular-nums">
          <span data-testid="analysis-history-size">
            {t('analysis.library.files', { count: formatCount(entry.fileCount) })}{' '}
            {t('analysis.library.lines', {
              added: formatCount(entry.linesAdded),
              removed: formatCount(entry.linesRemoved),
            })}
          </span>
          <span>{t('analysis.library.toolCalls', { count: formatCount(entry.toolCallCount) })}</span>
          <span>{t('analysis.library.duration', { seconds: Math.round(entry.durationMs / 1000) })}</span>
          <span data-testid="analysis-history-cost">
            {entry.costUsd === undefined
              ? t('analysis.costUnknown')
              : t('analysis.cost', { cost: Number(entry.costUsd).toFixed(4) })}
          </span>
        </p>
      </div>

      <div className="flex shrink-0 items-center gap-1">
        {!current && (
          <Button size="sm" variant="outline" disabled={busy} onClick={onOpen} data-testid="analysis-history-open">
            {t('analysis.library.open')}
          </Button>
        )}
        <button
          type="button"
          onClick={onDelete}
          aria-label={t('analysis.library.deleteLabel', { date })}
          data-testid="analysis-history-delete"
          className="rounded p-1.5 text-muted-foreground hover:bg-accent hover:text-destructive"
        >
          <Trash2Icon className="size-4" aria-hidden />
        </button>
      </div>
    </li>
  );
}

/** The same rendering the header's provenance line uses, so a run reads the same in both places. */
function formatDate(atUtc: string): string {
  return new Date(atUtc).toLocaleString();
}
