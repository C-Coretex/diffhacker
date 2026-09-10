import { useCallback, useEffect } from 'react';
import type {
  AnalysisNodeInfo,
  ChangedFileFactsInfo,
  EditorSettings,
  OpenInEditorRequestEditor,
} from '@/contracts';
import { describeError } from '@/i18n/errors';
import { describeEditors, openInEditor } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';

/**
 * Which external editors this machine actually has — requirement 3, asked once.
 *
 * Asked once for the life of the window, and the host caches its own probe behind that, so the two
 * surfaces that offer these buttons — the boxes on the diagram and the diff panel's header — can both
 * call this without either of them owning it or a second round trip happening.
 *
 * Not being able to ask is the same outcome as having nothing installed: no buttons. It is not worth
 * an alert on a screen whose job is showing a diff, and requirement 3's "handle VS Code not being
 * installed" is handled by never drawing a button that cannot work.
 */
export function useEditors(): EditorSettings | undefined {
  const client = useRpc();
  const editors = useAppStore((state) => state.editors);
  const setEditors = useAppStore((state) => state.setEditors);

  useEffect(() => {
    if (!client || editors) return;

    describeEditors(client)
      .then(setEditors)
      .catch(() => {
        // Deliberately silent. See above.
      });
  }, [client, editors, setEditors]);

  return editors;
}

/** What the caller has to know about one file to hand it to an editor. */
export interface EditorTarget {
  readonly node: AnalysisNodeInfo;
  readonly facts: ChangedFileFactsInfo | undefined;
  readonly repositoryPath: string;
}

/**
 * Hands one file to one editor.
 *
 * No command line is built here. The renderer names an editor, a file and a line; whether that
 * becomes a two-sided comparison or a single file at a line is the host's decision, made from which
 * sides of the file exist — which is how an added file, with no committed side, opens sensibly
 * instead of being compared against emptiness.
 *
 * The failure goes to the store rather than to local state, because the button that failed may be on
 * a 260-pixel box with nowhere to put a sentence. One message, one place it is shown.
 */
export function useOpenInEditor(): (
  target: EditorTarget,
  editor: OpenInEditorRequestEditor,
) => void {
  const client = useRpc();
  const setEditorError = useAppStore((state) => state.setEditorError);

  return useCallback(
    ({ node, facts, repositoryPath }: EditorTarget, editor: OpenInEditorRequestEditor) => {
      if (!client) return;

      setEditorError(undefined);

      openInEditor(client, {
        editor,
        line: node.startLine,
        path: node.filePath,
        // The committed side of a renamed file is at the old path. Sending it is what makes the
        // external comparison show the move rather than an addition and a deletion.
        previousPath: facts?.previousPath,
        repositoryPath,
      }).catch((caught: unknown) => setEditorError(describeError(caught)));
    },
    [client, setEditorError],
  );
}

/** One button per editor that is installed, and nothing for one that is not. */
export interface EditorChoice {
  readonly editor: OpenInEditorRequestEditor;

  /** Resource key for the editor's name, so nothing here holds English. */
  readonly labelKey: 'analysis.diff.openInVsCode' | 'analysis.diff.openInVisualStudio' | 'analysis.diff.openInCustom';

  /** A short form for the boxes on the diagram, which have a fifth of the width. */
  readonly shortKey: 'analysis.diff.shortVsCode' | 'analysis.diff.shortVisualStudio' | 'analysis.diff.shortCustom';
}

/**
 * The editors worth offering, in one list both surfaces read.
 *
 * A dead button that explains itself when pressed is worse than no button, and the host already knows
 * the answer because it went looking. The custom entry appears only when a command was configured.
 */
export function editorChoices(editors: EditorSettings | undefined): readonly EditorChoice[] {
  if (!editors) return [];

  return [
    ...(editors.vsCodeAvailable
      ? [
          {
            editor: 'vscode' as const,
            labelKey: 'analysis.diff.openInVsCode' as const,
            shortKey: 'analysis.diff.shortVsCode' as const,
          },
        ]
      : []),
    ...(editors.visualStudioAvailable
      ? [
          {
            editor: 'visual_studio' as const,
            labelKey: 'analysis.diff.openInVisualStudio' as const,
            shortKey: 'analysis.diff.shortVisualStudio' as const,
          },
        ]
      : []),
    ...(editors.customDiffCommand || editors.customOpenCommand
      ? [
          {
            editor: 'custom' as const,
            labelKey: 'analysis.diff.openInCustom' as const,
            shortKey: 'analysis.diff.shortCustom' as const,
          },
        ]
      : []),
  ];
}
