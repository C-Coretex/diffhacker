import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { node, testView } from '@/graph/testGraph';
import { useAppStore } from '@/store/appStore';
import { RiskList } from './RiskList';
import { InlineMarkdown, Markdown, MarkdownReferences, referenceResolver } from './Markdown';

/**
 * The model's prose, drawn.
 *
 * The parser has its own tests in `lib/markdown.test.ts`. What is checked here is what reaches the
 * DOM: the right elements, nothing that could navigate, and a file name that opens its diff.
 */
describe('Markdown', () => {
  beforeEach(() => {
    useAppStore.setState({
      diffNodeId: undefined,
      diffContainerId: undefined,
      graphFocusedNodeId: undefined,
      graphCollapsed: new Set<string>(),
    });
  });

  it('draws bold, italic, code, lists and code blocks as elements', () => {
    const { container } = render(
      <Markdown
        text={
          '**Cache key** now includes the *tenant*:\n- `CacheKey`\n- `TenantScope`\n\n```cs\nvar k = key;\n```'
        }
      />,
    );

    expect(container.querySelector('strong')).toHaveTextContent('Cache key');
    expect(container.querySelector('em')).toHaveTextContent('tenant');
    expect(container.querySelectorAll('ul > li')).toHaveLength(2);
    expect(screen.getByText('CacheKey').tagName).toBe('CODE');
    expect(container.querySelector('pre code')).toHaveTextContent('var k = key;');
  });

  it('draws plain text exactly as it was, line breaks included', () => {
    // Analyses stored before the model was asked for Markdown must read as they always did.
    const { container } = render(<Markdown text={'First line.\nSecond line.'} />);

    expect(container.querySelector('p')).toHaveTextContent('First line.Second line.');
    expect(container.querySelector('br')).not.toBeNull();
  });

  it('never draws a link that could navigate, nor markup it was handed', () => {
    // §0.2.13: the WebView must never leave diffhacker://app, and a URL the model wrote is not one
    // the reviewer chose to visit.
    const { container } = render(
      <Markdown text={'See [the RFC](https://example.com/rfc) and <img src=x onerror=alert(1)>'} />,
    );

    expect(container.querySelector('a')).toBeNull();
    expect(container.querySelector('img')).toBeNull();
    expect(screen.getByText('the RFC')).toHaveAttribute('title', 'https://example.com/rfc');
    expect(container).toHaveTextContent('<img src=x onerror=alert(1)>');
  });

  it('draws a risk with inline formatting and no block structure', () => {
    const { container } = render(<RiskList risks={['**Breaks** callers of `Load`\n- not a list']} />);

    expect(container.querySelector('strong')).toHaveTextContent('Breaks');
    expect(screen.getByText('Load').tagName).toBe('CODE');
    // One entry in the risk column stays one entry: no list inside it, no paragraph.
    expect(container.querySelectorAll('li')).toHaveLength(1);
    expect(container.querySelector('li p, li ul, li pre')).toBeNull();
  });

  it('leaves every code span as text when there is no analysis to point into', () => {
    render(<InlineMarkdown text="`src/Contract.cs`" />);

    expect(screen.queryByTestId('markdown-reference')).toBeNull();
    expect(screen.getByText('src/Contract.cs').tagName).toBe('CODE');
  });

  it('opens the diff of a file the prose names', async () => {
    const view = testView();

    render(
      <MarkdownReferences view={view}>
        <Markdown text={'Read `src/Caller.cs:12` next, then [the notes](src/Notes.md), not `Other.cs`.'} />
      </MarkdownReferences>,
    );

    const references = screen.getAllByTestId('markdown-reference');
    expect(references.map((r) => r.dataset.nodeId)).toEqual(['src/Caller.cs', 'src/Notes.md']);
    expect(references[0]).toHaveAttribute('title', 'Open the diff of src/Caller.cs');

    // Named but not in the analysis: text, never a button that goes nowhere.
    expect(screen.getByText('Other.cs').tagName).toBe('CODE');

    await userEvent.click(references[0]!);

    expect(useAppStore.getState().diffNodeId).toBe('src/Caller.cs');
    expect(useAppStore.getState().graphFocusedNodeId).toBe('src/Caller.cs');
  });

  it('turns a file named inside a risk into a reference too', () => {
    render(
      <MarkdownReferences view={testView()}>
        <RiskList risks={['Every caller of `src/Contract.cs` must be rebuilt.']} />
      </MarkdownReferences>,
    );

    expect(within(screen.getByRole('listitem')).getByTestId('markdown-reference')).toHaveTextContent(
      'src/Contract.cs',
    );
  });
});

describe('referenceResolver', () => {
  const resolve = referenceResolver([
    node('src/Cache.cs', 'core', 1, ['changed']),
    { ...node('src/Cache.cs#eviction', 'core', 2, ['changed']), filePath: 'src/Cache.cs' },
    node('src/index.ts', 'core', 3, ['changed']),
  ]);

  it('finds a node by its path, its id, and the spellings a model uses for a line', () => {
    expect(resolve('src/Cache.cs')?.id).toBe('src/Cache.cs');
    expect(resolve('src/Cache.cs#eviction')?.id).toBe('src/Cache.cs#eviction');
    expect(resolve('./src/Cache.cs')?.id).toBe('src/Cache.cs');
    expect(resolve('src\\Cache.cs')?.id).toBe('src/Cache.cs');
    expect(resolve('src/Cache.cs:42')?.id).toBe('src/Cache.cs');
    expect(resolve('src/Cache.cs:12-40')?.id).toBe('src/Cache.cs');
    expect(resolve('src/Cache.cs#L12')?.id).toBe('src/Cache.cs');
  });

  it('matches nothing it cannot be sure of', () => {
    // A basename names a dozen files in most repositories; opening the wrong one is worse than none.
    expect(resolve('index.ts')).toBeUndefined();
    expect(resolve('Cache.cs')).toBeUndefined();
    expect(resolve('CacheKey')).toBeUndefined();
    expect(resolve('')).toBeUndefined();
  });
});
