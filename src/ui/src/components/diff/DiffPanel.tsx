import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type * as Monaco from 'monaco-editor/esm/vs/editor/editor.api.js';
import {
  ChevronDownIcon,
  ChevronUpIcon,
  CircleCheckBigIcon,
  CircleIcon,
  ColumnsIcon,
  FoldVerticalIcon,
  Loader2Icon,
  MaximizeIcon,
  MinimizeIcon,
  RowsIcon,
  SaveIcon,
  TriangleAlertIcon,
  UnfoldVerticalIcon,
  XIcon,
} from 'lucide-react';
import type {
  AnalysisNodeInfo,
  AnalysisView,
  ChangedFileFactsInfo,
  FileContentInfo,
} from '@/contracts';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { fileContent, saveFileContent } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { useTheme } from '@/theme/useTheme';
import { Badge } from '@/components/ui/badge';
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
import { ChangeStats, NodeExplanation } from '@/components/analysis/NodeExplanation';
import { ContainerStrip } from './ContainerStrip';
import { describeContent, formatBytes, type DiffContent } from './diffContent';
import { DiffUnavailable } from './DiffUnavailable';
import { MonacoDiff } from './MonacoDiff';
import { NodeNavigator } from './NodeNavigator';
import { OpenInEditorButtons } from './OpenInEditorButtons';
import { ReadingOrderNav } from './ReadingOrderNav';
import { useReviewMarks } from './useReviewMarks';
import { useVerticalSplitter } from './useVerticalSplitter';

/**
 * The diff panel — the half of a review that used to happen in another window.
 *
 * ```
 * ┌──────────────────────────────────────────────┐
 * │ title · path · badges              [⛶] [✕]   │  header, and the two ways out
 * │ +12 −3 · modified · C#   [‹ Prev] 4/6 [Next ›]│  requirement 5's linear path
 * │ [◫][≡] [whole file] [VS Code] [✓ Mark read]  │  what to do with this file
 * ├──────────────────────────────────────────────┤
 * │ ▸ The cache cluster · 12 files · a b c d …   │  when a cluster was opened whole
 * ├──────────────────────────────────────────────┤
 * │                                              │
 * │            M O N A C O   D I F F             │  or a statement, for the
 * │                                              │  kinds that have no diff
 * ├──────────────────────────────────────────────┤
 * │ ▾ What changed and why               │ RISKS │  requirement 4, foldable
 * ├──────────────────────────────────────────────┤
 * │ read before ▲   ·   read after ▼             │  requirement 5's choice
 * └──────────────────────────────────────────────┘
 * ```
 *
 * The explanation stays under the code rather than behind a tab, because requirement 4 is that "the
 * reviewer should not return to the graph to remember why they are here" — and a tab is a return
 * trip with a shorter walk. It folds, though, and the fold is remembered across files: a reviewer who
 * has read the prose and wants the height for code has said something about how they read, not about
 * that one file. It is the same component the hover card draws, so the two cannot drift.
 *
 * Its height is also draggable, the same way the panel's own width is (`useSplitter`,
 * `useVerticalSplitter` here): a reviewer weighing a long explanation against a wide diff should be
 * able to trade one for the other rather than live with whatever fraction of the panel it defaulted
 * to. `DIFF_EXPLANATION_MIN_CODE` keeps Monaco from being dragged away entirely, the same way
 * `DIFF_PANEL_MIN_GRAPH` keeps a rail of diagram on screen.
 *
 * Content comes from two `changeset.fileContent` calls rather than from `changeset.fileDiff`: a
 * `DiffEditor` compares two texts, and the two sides already answer every awkward case requirement 8
 * lists — a deleted file is absent on one side, an added one on the other, and binary and too-large
 * are their own answers with the true size attached.
 */
export function DiffPanel({ view }: { view: AnalysisView }) {
  const t = useT();
  const client = useRpc();
  const theme = useTheme();

  const nodeId = useAppStore((state) => state.diffNodeId);
  const closeDiff = useAppStore((state) => state.closeDiff);
  const reviewedError = useAppStore((state) => state.reviewedError);
  const fullScreen = useAppStore((state) => state.diffFullScreen);
  const setFullScreen = useAppStore((state) => state.setDiffFullScreen);
  const explanationHeight = useAppStore((state) => state.diffExplanationHeight);
  const setExplanationHeight = useAppStore((state) => state.setDiffExplanationHeight);
  const dirty = useAppStore((state) => state.diffDirty);
  const setDirty = useAppStore((state) => state.setDiffDirty);
  const pendingNavigation = useAppStore((state) => state.diffPendingNavigation);
  const confirmDiscardNavigation = useAppStore((state) => state.confirmDiscardNavigation);
  const cancelPendingNavigation = useAppStore((state) => state.cancelPendingNavigation);

  const marks = useReviewMarks(view.repositoryPath);

  const [sideBySide, setSideBySide] = useState(true);
  const [content, setContent] = useState<DiffContent | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string>();
  const editor = useRef<Monaco.editor.IStandaloneDiffEditor | null>(null);
  const panel = useRef<HTMLDivElement>(null);

  // The editor's own state, kept outside React so every keystroke does not re-render the panel.
  // baseline is what a save is refused against on disk (§0.2.12's second write path); modifiedText
  // is what a save actually sends. Both reset whenever a fresh file loads.
  const baseline = useRef('');
  const modifiedText = useRef('');
  const workingEncoding = useRef<string | undefined>(undefined);

  const explanationSplitter = useVerticalSplitter(panel, explanationHeight, setExplanationHeight);

  const node = useMemo(
    () => view.nodes.find((candidate) => candidate.id === nodeId),
    [view.nodes, nodeId],
  );

  const facts = useMemo(
    () => view.changedFiles.find((file) => file.path === node?.filePath),
    [view.changedFiles, node?.filePath],
  );

  const repositoryPath = view.repositoryPath;
  const path = node?.filePath;
  const previousPath = facts?.previousPath;
  const startLine = node?.startLine ?? 0;

  /**
   * Whether unchanged runs are folded away, and the reviewer's answer to that.
   *
   * Starts where requirement 1 needs it — folded for a whole-file node, unfolded for one that names
   * lines, since folding is exactly what would hide the lines it names — and follows the file rather
   * than the session, because "show me the whole of *this* file" is a question about this file.
   */
  const [hideUnchanged, setHideUnchanged] = useState(startLine <= 0);

  useEffect(() => {
    setHideUnchanged(startLine <= 0);
  }, [nodeId, startLine]);

  // Both sides at once. Fetched when the node changes and not before: §0.2.8 forbids showing a
  // half-built result, but reading one file the reviewer just asked for is not building anything —
  // it is the click they made.
  useEffect(() => {
    if (!client || !path) {
      setContent(null);
      return;
    }

    let cancelled = false;

    setLoading(true);
    setError(undefined);
    setSaveError(undefined);

    const read = (target: string, side: 'head' | 'working_tree'): Promise<FileContentInfo> =>
      fileContent(client, { repositoryPath, path: target, side });

    Promise.all([
      // The committed side of a renamed file is at the path it had before it moved.
      read(previousPath ?? path, 'head'),
      read(path, 'working_tree'),
    ])
      .then(([head, working]) => {
        if (cancelled) return;

        const merged = describeContent(head, working);
        setContent(merged);

        // A fresh load is never dirty, whatever the previous file's editor state was. The working
        // tree's own encoding — not fallbackEncoding, which is silent for plain UTF-8 — is what a
        // save has to round-trip.
        baseline.current = merged.modified ?? '';
        modifiedText.current = baseline.current;
        workingEncoding.current = working.encoding;
        setDirty(false);
      })
      .catch((caught: unknown) => {
        if (cancelled) return;
        setError(describeError(caught));
        setContent(null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [client, repositoryPath, path, previousPath, setDirty]);

  // The editor's own callbacks. Refs rather than state: a keystroke should not re-render the
  // panel, and the save handler always wants the latest text regardless of when it was defined.
  const onModifiedChange = useCallback(
    (text: string) => {
      modifiedText.current = text;
      if (!dirty) setDirty(true);
    },
    [dirty, setDirty],
  );

  const handleSave = useCallback(() => {
    if (!client || !path || !dirty || saving) return;

    setSaving(true);
    setSaveError(undefined);

    saveFileContent(client, {
      repositoryPath,
      path,
      content: modifiedText.current,
      expectedContent: baseline.current,
      encoding: workingEncoding.current,
    })
      .then(() => {
        // The new baseline is what was just sent, not a round trip through the host: the bytes on
        // disk are TextDecoding.Encode(modifiedText.current, encoding), and decoding those back
        // would only reproduce the same string this already holds. `content` itself is left alone —
        // Monaco's own modified model already shows exactly this text, so there is nothing to
        // re-render and no reason to force MonacoDiff's model-swap effect to run again.
        baseline.current = modifiedText.current;
        setDirty(false);
      })
      .catch((caught: unknown) => {
        setSaveError(describeError(caught));
      })
      .finally(() => {
        setSaving(false);
      });
  }, [client, path, repositoryPath, dirty, saving, setDirty]);

  // Ctrl/Cmd+S works from inside Monaco through its own command (MonacoDiff), and from anywhere
  // else in the panel through this — both so the shortcut is not scoped to wherever focus happens
  // to be, and so the WebView's own save-page handling never gets a chance to fire instead.
  useEffect(() => {
    if (!nodeId) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
        event.preventDefault();
        handleSave();
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [nodeId, handleSave]);

  const onReady = useCallback((instance: Monaco.editor.IStandaloneDiffEditor | null) => {
    editor.current = instance;
  }, []);

  // Requirement 2's hunk-to-hunk navigation. Monaco owns the notion of "the changes in this diff",
  // so this asks it rather than counting hunks a second time from a patch nobody fetched.
  const goToChange = useCallback((direction: 'previous' | 'next') => {
    const instance = editor.current;
    const modified = instance?.getModifiedEditor();
    const changes = instance?.getLineChanges();
    if (!instance || !modified || !changes || changes.length === 0) return;

    const line = modified.getPosition()?.lineNumber ?? 1;
    const lines = changes.map((change) => Math.max(change.modifiedStartLineNumber, 1));

    // Wraps at both ends: a reviewer at the last change who presses next expects the first one, not
    // a button that stops working.
    const target =
      (direction === 'next'
        ? (lines.find((candidate) => candidate > line) ?? lines[0])
        : ([...lines].reverse().find((candidate) => candidate < line) ?? lines.at(-1))) ?? 1;

    modified.setPosition({ lineNumber: target, column: 1 });
    modified.revealLineInCenter(target);
  }, []);

  if (!node) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-1 p-6 text-center">
        <p className="text-sm text-muted-foreground">{t('analysis.diff.empty')}</p>
        <p className="text-xs text-muted-foreground">{t('analysis.diff.emptyHint')}</p>
      </div>
    );
  }

  const reviewed = marks.isReviewed(node.id);

  return (
    <section
      ref={panel}
      className="flex h-full min-h-0 flex-col overflow-hidden"
      data-testid="diff-panel"
      data-node-id={node.id}
      data-full-screen={fullScreen ? 'true' : 'false'}
      aria-label={t('analysis.diff.heading')}
    >
      <Header
        view={view}
        node={node}
        facts={facts}
        repositoryPath={repositoryPath}
        reviewed={reviewed}
        sideBySide={sideBySide}
        fullScreen={fullScreen}
        hideUnchanged={hideUnchanged}
        dirty={dirty}
        saving={saving}
        onToggleLayout={() => setSideBySide((current) => !current)}
        onToggleReviewed={() => marks.mark([node.id], !reviewed)}
        onToggleFullScreen={() => setFullScreen(!fullScreen)}
        onToggleHideUnchanged={() => setHideUnchanged((current) => !current)}
        onGoToChange={goToChange}
        onClose={closeDiff}
        onSave={handleSave}
        hasDiff={content?.kind === 'text'}
      />

      {/* Requirement: opening a cluster opens every file in it. Drawn only when one was opened. */}
      <ContainerStrip view={view} nodeId={node.id} />

      {reviewedError && (
        <p role="alert" className="shrink-0 px-3 py-1.5 text-xs text-destructive">
          {reviewedError}
        </p>
      )}

      {saveError && (
        <p role="alert" className="shrink-0 px-3 py-1.5 text-xs text-destructive" data-testid="save-error">
          {saveError}
        </p>
      )}

      <div className="min-h-0 flex-1 overflow-hidden">
        {loading && (
          <p
            className="flex h-full items-center justify-center gap-2 text-sm text-muted-foreground"
            aria-live="polite"
          >
            <Loader2Icon className="size-4 animate-spin" aria-hidden />
            {t('analysis.diff.loading')}
          </p>
        )}

        {!loading && error && (
          <p role="alert" className="flex h-full items-center justify-center p-6 text-sm text-destructive">
            {error}
          </p>
        )}

        {!loading && !error && content && (
          <div className="flex h-full min-h-0 flex-col">
            <Notices content={content} />

            {content.kind === 'text' ? (
              <div className="min-h-0 flex-1">
                <MonacoDiff
                  content={content}
                  path={node.filePath}
                  previousPath={previousPath}
                  sideBySide={sideBySide}
                  startLine={node.startLine}
                  endLine={node.endLine}
                  hideUnchanged={hideUnchanged}
                  theme={theme}
                  onReady={onReady}
                  onModifiedChange={onModifiedChange}
                  onSave={handleSave}
                />
              </div>
            ) : (
              <DiffUnavailable
                content={content}
                path={node.filePath}
                previousPath={previousPath}
                status={facts?.status}
              />
            )}
          </div>
        )}
      </div>

      {/*
        The divider that trades code height for explanation height, and back. A separator rather
        than a button, the same as the panel's own: it only moves, and it has no state of its own to
        announce beyond the drag in progress.
      */}
      <div
        role="separator"
        aria-orientation="horizontal"
        aria-label={t('analysis.diff.resizeExplanationHandle')}
        data-testid="diff-explanation-splitter"
        data-dragging={explanationSplitter.dragging ? 'true' : 'false'}
        onPointerDown={explanationSplitter.onPointerDown}
        className={
          explanationSplitter.dragging
            ? 'h-1 shrink-0 cursor-row-resize bg-primary'
            : 'h-1 shrink-0 cursor-row-resize bg-border hover:bg-primary/60'
        }
      />

      {/*
        Requirement 4 and requirement 5, below the code and always on screen. Scrolls on its own so
        a long explanation never takes more than the reviewer dragged it to, and never pushes the
        navigation off the bottom of a panel the reviewer is trying to leave. The height is theirs to
        set — a long explanation or a cramped laptop screen wants a different split than the default.
      */}
      <div
        data-testid="diff-explanation-panel"
        className="shrink-0 overflow-y-auto border-t border-border"
        style={{ height: explanationHeight }}
      >
        <Explanation node={node} />
        <NodeNavigator view={view} nodeId={node.id} />

        <p className="border-t border-border px-3 py-2 text-[10px] text-muted-foreground">
          {t('analysis.diff.shortcuts')}
        </p>
      </div>

      {/*
        The one place an edit can be lost: closing the panel, opening another file, or opening a
        whole cluster all go through the same guarded store actions (appStore.ts), so this one
        dialog covers every way of leaving a dirty file rather than each caller asking first.
      */}
      <AlertDialog
        open={pendingNavigation !== undefined}
        onOpenChange={(next) => !next && cancelPendingNavigation()}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('analysis.diff.discardTitle')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('analysis.diff.discardBody', { path: node.filePath })}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('analysis.diff.discardCancel')}</AlertDialogCancel>
            <AlertDialogAction
              data-testid="diff-confirm-discard"
              onClick={confirmDiscardNavigation}
            >
              {t('analysis.diff.discardConfirm')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}

/**
 * Requirement 4, with a fold over it.
 *
 * The prose is what the reviewer came for the first time they open a file and dead weight the
 * twentieth, and on a laptop it is competing with the code for the same three hundred pixels. The
 * state is in the store rather than here so it survives moving to the next file — folding it is a
 * statement about how this reviewer reads, not about the file that happened to be open.
 */
function Explanation({ node }: { node: AnalysisNodeInfo }) {
  const t = useT();
  const open = useAppStore((state) => state.diffExplanationOpen);
  const setOpen = useAppStore((state) => state.setDiffExplanationOpen);

  return (
    <div data-testid="diff-explanation" data-open={open ? 'true' : 'false'}>
      <button
        type="button"
        onClick={() => setOpen(!open)}
        aria-expanded={open}
        data-testid="toggle-explanation"
        className="flex w-full items-center gap-1.5 px-3 py-2 text-left text-[10px] font-semibold uppercase tracking-wide text-muted-foreground hover:bg-accent/50"
      >
        {open ? (
          <ChevronDownIcon className="size-3.5" aria-hidden />
        ) : (
          <ChevronUpIcon className="size-3.5" aria-hidden />
        )}
        {t('analysis.diff.explanationHeading')}

        {/* Folded, the count is what says there is something under it worth opening. */}
        {!open && node.risks.length > 0 && (
          <span className="ml-auto flex items-center gap-1 text-destructive">
            <TriangleAlertIcon className="size-3" aria-hidden />
            <span className="tabular-nums">{node.risks.length}</span>
          </span>
        )}
      </button>

      {open && <NodeExplanation node={node} columns={false} className="px-3 pb-3" />}
    </div>
  );
}

function Header({
  view,
  node,
  facts,
  repositoryPath,
  reviewed,
  sideBySide,
  fullScreen,
  hideUnchanged,
  dirty,
  saving,
  hasDiff,
  onToggleLayout,
  onToggleReviewed,
  onToggleFullScreen,
  onToggleHideUnchanged,
  onGoToChange,
  onClose,
  onSave,
}: {
  view: AnalysisView;
  node: AnalysisNodeInfo;
  facts: ChangedFileFactsInfo | undefined;
  repositoryPath: string;
  reviewed: boolean;
  sideBySide: boolean;
  fullScreen: boolean;
  hideUnchanged: boolean;
  dirty: boolean;
  saving: boolean;
  hasDiff: boolean;
  onToggleLayout(): void;
  onToggleReviewed(): void;
  onToggleFullScreen(): void;
  onToggleHideUnchanged(): void;
  onGoToChange(direction: 'previous' | 'next'): void;
  onClose(): void;
  onSave(): void;
}) {
  const t = useT();

  return (
    <header className="flex shrink-0 flex-col gap-1.5 border-b border-border px-3 py-2">
      <div className="flex items-start gap-2">
        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
          <h2 className="truncate text-sm font-semibold" title={node.title}>
            {node.title}
            {dirty && (
              <span
                aria-hidden
                data-testid="diff-dirty-indicator"
                className="ml-1.5 inline-block size-1.5 rounded-full bg-primary align-middle"
              />
            )}
          </h2>

          <p className="break-all font-mono text-[11px] text-muted-foreground">
            {node.filePath}
            {/* Requirement 8: a rename shows both paths, not only where the file ended up. */}
            {facts?.previousPath && (
              <span data-testid="renamed-from">
                {' · '}
                {t('analysis.diff.renamedFrom', { path: facts.previousPath })}
              </span>
            )}
          </p>
        </div>

        {/*
          §0.2.12's second write path, from the editor's own header rather than only from Ctrl/Cmd+S:
          a reviewer who did not know the shortcut still needs a way to see an edit is unsaved and a
          button to save it. Disabled with nothing to save, so it never reads as "save failed silently".
        */}
        {hasDiff && (
          <button
            type="button"
            onClick={onSave}
            disabled={!dirty || saving}
            aria-label={t(saving ? 'analysis.diff.saving' : 'analysis.diff.save')}
            title={t(saving ? 'analysis.diff.saving' : 'analysis.diff.save')}
            data-testid="save-edit"
            data-dirty={dirty ? 'true' : 'false'}
            className={
              dirty
                ? 'flex shrink-0 items-center gap-1 rounded border border-primary bg-primary/10 px-2 py-1 text-[11px] font-medium text-primary hover:bg-primary/20 disabled:cursor-not-allowed disabled:opacity-60'
                : 'flex shrink-0 items-center gap-1 rounded border border-border px-2 py-1 text-[11px] text-muted-foreground disabled:cursor-not-allowed disabled:opacity-60'
            }
          >
            {saving ? (
              <Loader2Icon className="size-3.5 animate-spin" aria-hidden />
            ) : (
              <SaveIcon className="size-3.5" aria-hidden />
            )}
            {t(saving ? 'analysis.diff.saving' : 'analysis.diff.save')}
          </button>
        )}

        {/*
          Full screen, and the way back. The splitter's travel stops short of the left edge because
          requirement 6 wants the reviewer's position visible in the diagram while they drag; this is
          a mode they asked for explicitly, with its exit in the place they pressed to enter it.
        */}
        <button
          type="button"
          onClick={onToggleFullScreen}
          aria-pressed={fullScreen}
          aria-label={t(fullScreen ? 'analysis.diff.exitFullScreen' : 'analysis.diff.fullScreen')}
          title={t(fullScreen ? 'analysis.diff.exitFullScreen' : 'analysis.diff.fullScreen')}
          data-testid="toggle-full-screen"
          className="shrink-0 rounded p-1 text-muted-foreground hover:bg-accent hover:text-foreground"
        >
          {fullScreen ? (
            <MinimizeIcon className="size-4" aria-hidden />
          ) : (
            <MaximizeIcon className="size-4" aria-hidden />
          )}
        </button>

        <button
          type="button"
          onClick={onClose}
          aria-label={t('analysis.diff.close')}
          data-testid="close-diff"
          className="shrink-0 rounded p-1 text-muted-foreground hover:bg-accent hover:text-foreground"
        >
          <XIcon className="size-4" aria-hidden />
        </button>
      </div>

      <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
        <span className="text-[11px] text-muted-foreground">
          <ChangeStats facts={facts} />
        </span>

        <Badge variant="outline" className="px-1.5 py-0 text-[10px]">
          {node.startLine > 0
            ? t('analysis.diff.region', { start: node.startLine, end: node.endLine })
            : t('analysis.diff.wholeFile')}
        </Badge>

        {/* Requirement 5's linear path, at the top where it is pressed from. */}
        <div className="ml-auto">
          <ReadingOrderNav view={view} nodeId={node.id} />
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-1">
        {hasDiff && (
          <>
            <button
              type="button"
              onClick={() => onGoToChange('previous')}
              aria-label={t('analysis.diff.previousHunk')}
              title={t('analysis.diff.previousHunk')}
              data-testid="previous-hunk"
              className="rounded border border-border p-1 hover:bg-accent"
            >
              <ChevronUpIcon className="size-3.5" aria-hidden />
            </button>
            <button
              type="button"
              onClick={() => onGoToChange('next')}
              aria-label={t('analysis.diff.nextHunk')}
              title={t('analysis.diff.nextHunk')}
              data-testid="next-hunk"
              className="rounded border border-border p-1 hover:bg-accent"
            >
              <ChevronDownIcon className="size-3.5" aria-hidden />
            </button>

            <button
              type="button"
              onClick={onToggleLayout}
              aria-pressed={sideBySide}
              data-testid="toggle-layout"
              className="flex items-center gap-1 rounded border border-border px-2 py-1 text-[11px] hover:bg-accent"
            >
              {sideBySide ? (
                <ColumnsIcon className="size-3" aria-hidden />
              ) : (
                <RowsIcon className="size-3" aria-hidden />
              )}
              {t(sideBySide ? 'analysis.diff.sideBySide' : 'analysis.diff.inline')}
            </button>

            {/*
              The whole file, or only what changed with three lines around it. Monaco folds the
              unchanged runs by default and that is what makes a long diff readable — but a reviewer
              deciding whether a change is safe often needs the code it did *not* touch, and until now
              the only way to see it was to open the file somewhere else. Which is the thing this
              iteration exists to stop.
            */}
            <button
              type="button"
              onClick={onToggleHideUnchanged}
              aria-pressed={!hideUnchanged}
              data-testid="toggle-whole-file"
              className="flex items-center gap-1 rounded border border-border px-2 py-1 text-[11px] hover:bg-accent"
            >
              {hideUnchanged ? (
                <UnfoldVerticalIcon className="size-3" aria-hidden />
              ) : (
                <FoldVerticalIcon className="size-3" aria-hidden />
              )}
              {t(hideUnchanged ? 'analysis.diff.showWholeFile' : 'analysis.diff.showChangesOnly')}
            </button>
          </>
        )}

        <div className="ml-auto flex flex-wrap items-center gap-1">
          <OpenInEditorButtons repositoryPath={repositoryPath} node={node} facts={facts} />

          {/* Requirement 7. Works for every file kind, binary included: a node is a node. The same
              toggle is on the box itself, for a reviewer who is marking from the diagram. */}
          <button
            type="button"
            onClick={onToggleReviewed}
            aria-pressed={reviewed}
            data-testid="toggle-reviewed"
            className={
              reviewed
                ? 'flex items-center gap-1 rounded border border-primary bg-primary/10 px-2 py-1 text-[11px] font-medium'
                : 'flex items-center gap-1 rounded border border-border px-2 py-1 text-[11px] hover:bg-accent'
            }
          >
            {reviewed ? (
              <CircleCheckBigIcon className="size-3" aria-hidden />
            ) : (
              <CircleIcon className="size-3" aria-hidden />
            )}
            {t(reviewed ? 'analysis.diff.markUnreviewed' : 'analysis.diff.markReviewed')}
          </button>
        </div>
      </div>
    </header>
  );
}

/** What the viewer had to compromise on, said plainly rather than left for the reviewer to notice. */
function Notices({ content }: { content: DiffContent }) {
  const t = useT();

  if (!content.degraded && !content.fallbackEncoding) return null;

  return (
    <div className="shrink-0 border-b border-border bg-warning/10 px-3 py-1.5">
      {content.degraded && (
        <p className="text-[11px] text-muted-foreground" data-testid="degraded-notice">
          <TriangleAlertIcon className="mr-1 inline size-3 align-[-2px]" aria-hidden />
          {t('analysis.diff.degraded', { size: formatBytes(content.sizeBytes) })}
        </p>
      )}

      {content.fallbackEncoding && (
        <p className="text-[11px] text-muted-foreground" data-testid="encoding-notice">
          {t('analysis.diff.fallbackEncoding', { encoding: content.fallbackEncoding })}
        </p>
      )}
    </div>
  );
}
