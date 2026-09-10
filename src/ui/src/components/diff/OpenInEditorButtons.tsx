import { ExternalLinkIcon } from 'lucide-react';
import type { AnalysisNodeInfo, ChangedFileFactsInfo } from '@/contracts';
import { useT } from '@/i18n/useT';
import { editorChoices, useEditors, useOpenInEditor } from './useEditors';

/**
 * "Open in VS Code" and "Open in VS" — requirement 3, as the panel draws them.
 *
 * The same two buttons are on every box on the diagram (`graph/NodeActions.tsx`), which is where a
 * reviewer who has not opened the panel yet will look for them. Both go through `useOpenInEditor`, so
 * there is one implementation of "hand this file to that editor" and one place a failure is reported.
 */
export function OpenInEditorButtons({
  repositoryPath,
  node,
  facts,
}: {
  repositoryPath: string;
  node: AnalysisNodeInfo;
  facts: ChangedFileFactsInfo | undefined;
}) {
  const t = useT();
  const editors = useEditors();
  const open = useOpenInEditor();

  const choices = editorChoices(editors);
  if (choices.length === 0) return null;

  return (
    <div className="flex flex-wrap items-center gap-1">
      {choices.map(({ editor, labelKey }) => (
        <button
          key={editor}
          type="button"
          onClick={() => open({ node, facts, repositoryPath }, editor)}
          data-testid={`open-in-${editor}`}
          className="flex items-center gap-1 rounded border border-border px-2 py-1 text-[11px] hover:bg-accent"
        >
          <ExternalLinkIcon className="size-3" aria-hidden />
          {t('analysis.diff.openIn', { editor: t(labelKey) })}
        </button>
      ))}
    </div>
  );
}
