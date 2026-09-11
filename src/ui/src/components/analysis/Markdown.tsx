import { createContext, useContext, useMemo, type ReactNode } from 'react';
import type { AnalysisNodeInfo, AnalysisView } from '@/contracts';
import { useT } from '@/i18n/useT';
import { parseInline, parseMarkdown, type Block, type Inline } from '@/lib/markdown';
import { cn } from '@/lib/utils';
import { useAppStore } from '@/store/appStore';

/**
 * The model's prose, drawn as the Markdown it was asked to write.
 *
 * Two entry points because there are two kinds of text. `Markdown` is for explanations and
 * summaries, which may hold paragraphs, lists and code. `InlineMarkdown` is for a risk: one line
 * item in a column of them, where bold and code help but a list or a code block would turn the risk
 * back into a paragraph, and §0.1 keeps paragraphs out of the risk column.
 *
 * **A citation opens what it cites.** When a code span or a link names a file this analysis has a
 * node for — `src/Cache.cs`, `src/Cache.cs#eviction`, `src/Cache.cs:42` — it is drawn as a button
 * that opens that file's diff, which is what the reviewer would otherwise do by hand after reading
 * the name. Anything else stays text. A link's target is never an `href`, because a click that
 * navigated the WebView away from `diffhacker://app` would leave the reviewer with no application,
 * and a URL the model wrote is not one the reviewer chose to visit.
 */
export function Markdown({ text, className }: { text: string; className?: string }) {
  const blocks = useMemo(() => parseMarkdown(text), [text]);

  return (
    <div className={cn('flex min-w-0 flex-col gap-1.5', className)} data-testid="markdown">
      {blocks.map((block, index) => (
        <BlockView key={index} block={block} />
      ))}
    </div>
  );
}

/** Inline formatting only, for text that must stay one line item. */
export function InlineMarkdown({ text, className }: { text: string; className?: string }) {
  const inlines = useMemo(() => parseInline(text), [text]);

  return (
    <span className={cn('min-w-0', className)}>
      <Inlines nodes={inlines} />
    </span>
  );
}

function BlockView({ block }: { block: Block }) {
  switch (block.kind) {
    case 'paragraph':
      return (
        <p className="break-words">
          <Inlines nodes={block.children} />
        </p>
      );

    case 'code':
      return (
        <pre
          className="overflow-x-auto rounded bg-muted px-2 py-1.5 font-mono text-[0.85em] leading-snug"
          data-language={block.language || undefined}
        >
          <code>{block.text}</code>
        </pre>
      );

    case 'list': {
      const items = block.items.map((item, index) => (
        <li key={index} className="pl-0.5">
          <div className="flex min-w-0 flex-col gap-1">
            {item.map((child, childIndex) => (
              <BlockView key={childIndex} block={child} />
            ))}
          </div>
        </li>
      ));

      return block.ordered ? (
        <ol
          start={block.start === 1 ? undefined : block.start}
          className="flex list-decimal flex-col gap-0.5 pl-5 marker:text-muted-foreground"
        >
          {items}
        </ol>
      ) : (
        <ul className="flex list-disc flex-col gap-0.5 pl-4 marker:text-muted-foreground">
          {items}
        </ul>
      );
    }
  }
}

function Inlines({ nodes }: { nodes: readonly Inline[] }): ReactNode {
  return nodes.map((node, index) => <InlineView key={index} node={node} />);
}

const codeClass =
  'rounded bg-muted px-1 py-px font-mono text-[0.85em] [overflow-wrap:anywhere]';

function InlineView({ node }: { node: Inline }) {
  const resolve = useContext(ReferenceContext);

  switch (node.kind) {
    case 'text':
      return node.text;

    case 'break':
      return <br />;

    case 'strong':
      return (
        <strong className="font-semibold">
          <Inlines nodes={node.children} />
        </strong>
      );

    case 'emphasis':
      return (
        <em>
          <Inlines nodes={node.children} />
        </em>
      );

    case 'code': {
      const target = resolve?.(node.text);
      return target ? (
        <Reference node={target} className={codeClass}>
          {node.text}
        </Reference>
      ) : (
        <code className={codeClass}>{node.text}</code>
      );
    }

    case 'link': {
      const target = resolve?.(node.target);
      return target ? (
        <Reference node={target}>
          <Inlines nodes={node.children} />
        </Reference>
      ) : (
        // The text the model linked, with where it pointed on hover. Nothing to click: see above.
        <span title={node.target}>
          <Inlines nodes={node.children} />
        </span>
      );
    }
  }
}

/** A file the prose names and the analysis has a node for, as a way to open it. */
function Reference({
  node,
  className,
  children,
}: {
  node: AnalysisNodeInfo;
  className?: string;
  children: ReactNode;
}) {
  const t = useT();
  const openDiff = useAppStore((state) => state.openDiffFor);

  return (
    <button
      type="button"
      onClick={() => openDiff(node.id, node.containerId)}
      title={t('analysis.markdown.openReference', { path: node.filePath })}
      data-testid="markdown-reference"
      data-node-id={node.id}
      className={cn(
        'text-left text-primary underline decoration-dotted underline-offset-2 hover:decoration-solid',
        className,
      )}
    >
      {children}
    </button>
  );
}

// ---------------------------------------------------------------------------------------------
// References

type ResolveReference = (text: string) => AnalysisNodeInfo | undefined;

const ReferenceContext = createContext<ResolveReference | undefined>(undefined);

/**
 * Lets prose under it turn file names into buttons, for the analysis on screen.
 *
 * One index for the whole screen rather than one per paragraph: a card renders a dozen code spans
 * and the diagram has three hundred cards' worth of them. Without a provider — in a unit test, or
 * anywhere prose is shown without an analysis — nothing is a reference and everything is text.
 */
export function MarkdownReferences({ view, children }: { view: AnalysisView; children: ReactNode }) {
  const resolve = useMemo(() => referenceResolver(view.nodes), [view.nodes]);
  return <ReferenceContext.Provider value={resolve}>{children}</ReferenceContext.Provider>;
}

/**
 * Finds the node a piece of text names, or nothing.
 *
 * Exact matches only, after the spellings a model uses for "this file, around here" are taken
 * away: a leading `./`, backslashes, and a line suffix such as `:42`, `:12-40` or `#L12`. A node id
 * (`src/Cache.cs#eviction`) finds that node; a bare path finds the file's first node in reading
 * order. A basename alone does not match — `index.ts` names a dozen files in most repositories, and
 * a reference that opened the wrong one would be worse than none.
 */
export function referenceResolver(nodes: readonly AnalysisNodeInfo[]): ResolveReference {
  const byId = new Map<string, AnalysisNodeInfo>();
  const byPath = new Map<string, AnalysisNodeInfo>();

  for (const node of nodes) {
    byId.set(node.id, node);
    if (!byPath.has(node.filePath)) byPath.set(node.filePath, node);
  }

  return (text) => {
    const trimmed = text.trim().replace(/\\/g, '/').replace(/^\.\//, '');
    if (!trimmed) return undefined;

    const exact = byId.get(trimmed) ?? byPath.get(trimmed);
    if (exact) return exact;

    const withoutLines = trimmed.replace(/(?::\d+(?:[-:]\d+)?|#L\d+(?:-L?\d+)?)$/, '');
    return withoutLines === trimmed ? undefined : (byId.get(withoutLines) ?? byPath.get(withoutLines));
  };
}
