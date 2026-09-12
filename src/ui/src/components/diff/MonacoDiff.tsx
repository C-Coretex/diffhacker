import { useEffect, useRef } from 'react';
import type * as Monaco from 'monaco-editor/esm/vs/editor/editor.api.js';
import { monaco } from './monaco/setup';
import type { DiffContent } from './diffContent';

export interface MonacoDiffProps {
  readonly content: DiffContent;

  /** Repository-relative path of the working-tree side. Monaco reads the language off it. */
  readonly path: string;

  /** Where the file was before it moved, so a rename compares the right two things. */
  readonly previousPath?: string;

  /** True for side-by-side, false for inline. Requirement 2. */
  readonly sideBySide: boolean;

  /** First line of the node's region, counting from 1. Zero when the node is the whole file. */
  readonly startLine: number;

  /** Last line of the region. Zero when there is no range. */
  readonly endLine: number;

  /**
   * Whether unchanged runs are folded away.
   *
   * The panel decides it, because it is a control the reviewer can reach: folding is what makes a
   * two-thousand-line diff readable, and unfolding is what "show me the whole file" means.
   */
  readonly hideUnchanged: boolean;

  readonly theme: 'light' | 'dark';

  /** Handed the editor so the panel's hunk buttons can drive it. Called again on every rebuild. */
  readonly onReady?: (editor: Monaco.editor.IStandaloneDiffEditor | null) => void;

  /**
   * Called with the working-tree side's text on every keystroke, so the panel can track whether
   * there is an edit to save. Not called for the model Monaco creates itself on load — only for a
   * change the reviewer made — which is what keeps opening a file from reading as an edit of it.
   */
  readonly onModifiedChange?: (text: string) => void;

  /** Ctrl/Cmd+S from inside the editor. The panel decides whether there is anything to save. */
  readonly onSave?: () => void;
}

/**
 * Monaco's `DiffEditor`, wrapped as thinly as it can honestly be wrapped.
 *
 * Monaco owns a DOM subtree and a lifecycle of its own, so this is one of the few places in the
 * renderer that is imperative on purpose: React owns the container `div` and nothing inside it. A
 * declarative wrapper would mean re-creating the editor whenever a prop changed, and re-creating it is
 * the expensive thing — models are swapped instead.
 *
 * `@monaco-editor/react` exists and is not used. It loads Monaco from a CDN unless reconfigured, which
 * §0.2.13 and the Content-Security-Policy both forbid, and what remains after that configuration is
 * about as much code as this file.
 */
export function MonacoDiff({
  content,
  path,
  previousPath,
  sideBySide,
  startLine,
  endLine,
  hideUnchanged,
  theme,
  onReady,
  onModifiedChange,
  onSave,
}: MonacoDiffProps) {
  const host = useRef<HTMLDivElement>(null);
  const editor = useRef<Monaco.editor.IStandaloneDiffEditor | null>(null);
  const decorations = useRef<Monaco.editor.IEditorDecorationsCollection | null>(null);
  const modifiedListener = useRef<Monaco.IDisposable | null>(null);

  // Read from a ref rather than closed over, so the command and the listener registered once
  // below always call whatever the panel most recently passed, without the editor being rebuilt.
  const onSaveRef = useRef(onSave);
  onSaveRef.current = onSave;
  const onModifiedChangeRef = useRef(onModifiedChange);
  onModifiedChangeRef.current = onModifiedChange;

  // Created once for the life of the panel. Every prop below reaches it through an update rather than
  // a rebuild: at a megabyte of source, tearing the editor down and building it again is visible.
  useEffect(() => {
    if (!host.current) return;

    const created = monaco.editor.createDiffEditor(host.current, {
      automaticLayout: true,
      // The committed side stays read-only always — nothing edits git history — but the reviewer
      // can type directly into the working-tree side and save it (§0.2.12's second write path).
      readOnly: false,
      originalEditable: false,
      renderSideBySide: true,
      scrollBeyondLastLine: false,
      renderOverviewRuler: true,
      fontSize: 12,
      lineNumbersMinChars: 4,
    });

    created
      .getModifiedEditor()
      .addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => onSaveRef.current?.());

    editor.current = created;
    decorations.current = created.getModifiedEditor().createDecorationsCollection([]);

    return () => {
      // The models go with it. Monaco does not dispose models a diff editor was merely showing, and
      // a review that walks three hundred nodes would otherwise leak three hundred pairs.
      const models = created.getModel();
      modifiedListener.current?.dispose();
      modifiedListener.current = null;
      created.dispose();
      models?.original.dispose();
      models?.modified.dispose();
      editor.current = null;
      decorations.current = null;
    };
  }, []);

  // Reported after creation and after every model swap, because the panel's hunk navigation acts on
  // whichever pair is loaded now.
  useEffect(() => {
    onReady?.(editor.current);
  }, [onReady, content]);

  useEffect(() => {
    monaco.editor.setTheme(theme === 'dark' ? 'vs-dark' : 'vs');
  }, [theme]);

  useEffect(() => {
    editor.current?.updateOptions({ renderSideBySide: sideBySide });
  }, [sideBySide]);

  // The models. Replaced whenever the file or its content changes, and the old pair disposed — a
  // model Monaco is no longer showing is still a model, and its text is still in memory.
  useEffect(() => {
    const instance = editor.current;
    if (!instance) return;

    const previous = instance.getModel();

    // Monaco resolves the language from the path, using the same extension table the eighty-one
    // language contributions registered. That is why there is no language map in this iteration: one
    // already exists, inside Monaco, and a second would drift from it.
    //
    // Above the size threshold the language is forced to plaintext instead. The diff is identical;
    // what is skipped is tokenising a megabyte through a Monarch grammar on the main thread.
    const language = content.degraded ? 'plaintext' : undefined;

    // Disposed before the new pair is created, not after: a save can leave `path`/`previousPath`
    // unchanged while `content` still gets a new identity (DiffPanel resets its dirty tracking from
    // it), and the new models below are addressed by the same URIs as the ones just shown. Monaco's
    // model service refuses to create a second model at a URI that is still registered, so creating
    // first and disposing the old pair after would throw on exactly that case.
    previous?.original.dispose();
    previous?.modified.dispose();

    const original = monaco.editor.createModel(
      content.original ?? '',
      language,
      monaco.Uri.parse(`diffhacker-head:/${encodeURI(previousPath ?? path)}`),
    );

    const modified = monaco.editor.createModel(
      content.modified ?? '',
      language,
      monaco.Uri.parse(`diffhacker-worktree:/${encodeURI(path)}`),
    );

    instance.updateOptions({
      minimap: { enabled: !content.degraded },
      wordWrap: content.degraded ? 'off' : 'on',
      // The overview ruler renders a mark per change. On a file with fifty thousand of them that is
      // fifty thousand marks nobody can aim at.
      renderOverviewRuler: !content.degraded,
    });

    instance.setModel({ original, modified });

    // Attached after setModel, so loading the file itself never reads as an edit of it — only a
    // change the reviewer makes from here on fires the panel's dirty tracking.
    modifiedListener.current?.dispose();
    modifiedListener.current = modified.onDidChangeContent(() => {
      onModifiedChangeRef.current?.(modified.getValue());
    });
  }, [content, path, previousPath]);

  /**
   * Requirement 2's expandable context, and the control the panel puts over it.
   *
   * Monaco's own: unchanged runs fold to three lines with a control to open them, which is what makes
   * a diff of a two-thousand-line file readable. It starts off for a node that names a place inside a
   * file — requirement 1 winning a genuine conflict, since the lines a node is *about* are usually
   * unchanged and folding is exactly what would hide them — and the panel's "show the whole file"
   * button flips it either way from there.
   *
   * Its own effect rather than part of the model swap, so pressing that button re-folds what is
   * already loaded instead of rebuilding two models to change one boolean.
   */
  useEffect(() => {
    editor.current?.updateOptions({
      hideUnchangedRegions: { enabled: hideUnchanged, contextLineCount: 3, minimumLineCount: 6 },
    });
  }, [hideUnchanged, content]);

  // Requirement 1's second half. A node that names a place inside a file has to open at that place:
  // opening at line 1 and leaving the reviewer to scroll is the alphabetical file list again, one
  // file smaller.
  useEffect(() => {
    const instance = editor.current;
    const collection = decorations.current;
    if (!instance || !collection) return;

    if (startLine <= 0) {
      collection.clear();
      return;
    }

    const last = Math.max(endLine, startLine);

    collection.set([
      {
        range: new monaco.Range(startLine, 1, last, 1),
        options: {
          isWholeLine: true,
          className: 'diffhacker-node-region',
          overviewRuler: {
            color: 'rgba(96, 165, 250, 0.6)',
            position: monaco.editor.OverviewRulerLane.Right,
          },
        },
      },
    ]);

    // Centred rather than scrolled-to-top, so the lines above the region — the context that makes it
    // readable — are on screen with it.
    instance.getModifiedEditor().revealLineInCenter(startLine);
    instance.getModifiedEditor().setPosition({ lineNumber: startLine, column: 1 });
  }, [startLine, endLine, content, path]);

  return (
    <div
      ref={host}
      className="h-full min-h-0 w-full"
      data-testid="monaco-diff"
      data-degraded={content.degraded ? 'true' : 'false'}
      data-side-by-side={sideBySide ? 'true' : 'false'}
      data-hide-unchanged={hideUnchanged ? 'true' : 'false'}
    />
  );
}
