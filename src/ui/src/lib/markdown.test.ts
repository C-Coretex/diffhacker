import { describe, expect, it } from 'vitest';
import { parseInline, parseMarkdown, plainText, type Block, type Inline } from './markdown';

const text = (value: string): Inline => ({ kind: 'text', text: value });

/** The first block, which the test expects to be a list. */
function listOf(source: string): Extract<Block, { kind: 'list' }> {
  const [block] = parseMarkdown(source);
  if (block?.kind !== 'list') throw new Error(`not a list: ${JSON.stringify(block)}`);
  return block;
}

describe('parseInline', () => {
  it('reads bold, italic and code', () => {
    expect(parseInline('**tenant** is *now* part of `CacheKey`')).toEqual([
      { kind: 'strong', children: [text('tenant')] },
      text(' is '),
      { kind: 'emphasis', children: [text('now')] },
      text(' part of '),
      { kind: 'code', text: 'CacheKey' },
    ]);
  });

  it('pairs each delimiter with its own when they nest', () => {
    expect(parseInline('**bold *and italic* bold**')).toEqual([
      {
        kind: 'strong',
        children: [text('bold '), { kind: 'emphasis', children: [text('and italic')] }, text(' bold')],
      },
    ]);
  });

  it('reads three asterisks as bold italic', () => {
    expect(parseInline('***both***')).toEqual([
      { kind: 'strong', children: [{ kind: 'emphasis', children: [text('both')] }] },
    ]);
  });

  it('leaves asterisks that open nothing as text', () => {
    // Arithmetic, a glob, and a delimiter that is never closed. None of them is emphasis, and all
    // of them must survive character for character.
    expect(parseInline('a * b * c')).toEqual([text('a * b * c')]);
    expect(parseInline('src/**/*.cs')).toEqual([text('src/**/*.cs')]);
    expect(parseInline('**unclosed')).toEqual([text('**unclosed')]);
  });

  it('never treats underscores as emphasis', () => {
    // What a model writes with underscores is identifiers, far more often than it is italics.
    expect(parseInline('__init__.py and snake_case_name and _private_')).toEqual([
      text('__init__.py and snake_case_name and _private_'),
    ]);
  });

  it('keeps everything inside a code span literal', () => {
    expect(parseInline('`a *b* c` then **d**')).toEqual([
      { kind: 'code', text: 'a *b* c' },
      text(' then '),
      { kind: 'strong', children: [text('d')] },
    ]);
  });

  it('does not let an asterisk inside code close emphasis outside it', () => {
    expect(parseInline('*see `x*y` here*')).toEqual([
      { kind: 'emphasis', children: [text('see '), { kind: 'code', text: 'x*y' }, text(' here')] },
    ]);
  });

  it('reads a longer backtick run so a code span can hold a backtick', () => {
    expect(parseInline('``a ` b``')).toEqual([{ kind: 'code', text: 'a ` b' }]);
    expect(parseInline('`` `tick` ``')).toEqual([{ kind: 'code', text: '`tick`' }]);
  });

  it('leaves an unmatched backtick as text', () => {
    expect(parseInline('a ` b')).toEqual([text('a ` b')]);
  });

  it('honours backslash escapes', () => {
    expect(parseInline('\\*not italic\\*')).toEqual([text('*not italic*')]);
  });

  it('turns a newline into a line break', () => {
    expect(parseInline('one\ntwo')).toEqual([text('one'), { kind: 'break' }, text('two')]);
  });

  it('keeps a link as its text and its target, and an image as its alt text', () => {
    expect(parseInline('see [the cache](src/Cache.cs "title") now')).toEqual([
      text('see '),
      { kind: 'link', target: 'src/Cache.cs', children: [text('the cache')] },
      text(' now'),
    ]);
    expect(parseInline('![diagram](https://example.com/a.png)')).toEqual([
      { kind: 'link', target: 'https://example.com/a.png', children: [text('diagram')] },
    ]);
  });

  it('leaves brackets that are not a link as text', () => {
    expect(parseInline('array[0] and [note]')).toEqual([text('array[0] and [note]')]);
  });

  it('never produces markup from markup-looking text', () => {
    // The tree is data. What React is later handed is a string, and it escapes it.
    expect(parseInline('<script>alert(1)</script>')).toEqual([text('<script>alert(1)</script>')]);
  });
});

describe('parseMarkdown', () => {
  it('reads a plain string as one paragraph', () => {
    expect(parseMarkdown('Cache key now includes the tenant.')).toEqual([
      { kind: 'paragraph', children: [text('Cache key now includes the tenant.')] },
    ]);
  });

  it('keeps single newlines as line breaks, the way plain-text analyses were written', () => {
    expect(parseMarkdown('first line\nsecond line')).toEqual([
      { kind: 'paragraph', children: [text('first line'), { kind: 'break' }, text('second line')] },
    ]);
  });

  it('splits paragraphs on blank lines', () => {
    expect(parseMarkdown('one\n\n\ntwo')).toHaveLength(2);
  });

  it('reads a bulleted list, including one that follows a sentence directly', () => {
    expect(parseMarkdown('Three callers changed:\n- `Api`\n- `Worker`\n* `Cli`')).toEqual([
      { kind: 'paragraph', children: [text('Three callers changed:')] },
      {
        kind: 'list',
        ordered: false,
        start: 1,
        items: [
          [{ kind: 'paragraph', children: [{ kind: 'code', text: 'Api' }] }],
          [{ kind: 'paragraph', children: [{ kind: 'code', text: 'Worker' }] }],
          [{ kind: 'paragraph', children: [{ kind: 'code', text: 'Cli' }] }],
        ],
      },
    ]);
  });

  it('reads a numbered list and keeps where it starts', () => {
    const list = listOf('3. third\n4) fourth');
    expect(list).toMatchObject({ ordered: true, start: 3 });
    expect(list.items).toHaveLength(2);
  });

  it('does not start a numbered list from a wrapped sentence', () => {
    expect(parseMarkdown('The flag shipped in\n2024. It is now removed.')).toEqual([
      {
        kind: 'paragraph',
        children: [text('The flag shipped in'), { kind: 'break' }, text('2024. It is now removed.')],
      },
    ]);
  });

  it('nests a list indented under an item', () => {
    const list = listOf('- outer\n  - inner\n  - inner two\n- second');

    expect(list.items).toHaveLength(2);
    expect(list.items[0]).toEqual([
      { kind: 'paragraph', children: [text('outer')] },
      {
        kind: 'list',
        ordered: false,
        start: 1,
        items: [
          [{ kind: 'paragraph', children: [text('inner')] }],
          [{ kind: 'paragraph', children: [text('inner two')] }],
        ],
      },
    ]);
  });

  it('keeps a wrapped bullet as one item', () => {
    const list = listOf('- a long bullet\nthat wrapped\n- next');

    expect(list.items[0]).toEqual([
      { kind: 'paragraph', children: [text('a long bullet'), { kind: 'break' }, text('that wrapped')] },
    ]);
  });

  it('ends a list at a paragraph after a blank line', () => {
    const blocks = parseMarkdown('- one\n- two\n\nAfter the list.');
    expect(blocks.map((block) => block.kind)).toEqual(['list', 'paragraph']);
  });

  it('continues a list across a blank line between items', () => {
    expect(parseMarkdown('- one\n\n- two')).toHaveLength(1);
    expect(listOf('- one\n\n- two').items).toHaveLength(2);
  });

  it('starts a new list when bullets turn into numbers', () => {
    expect(parseMarkdown('- a\n1. b').map((block) => block.kind)).toEqual(['list', 'list']);
  });

  it('reads a fenced code block verbatim, with its language', () => {
    expect(parseMarkdown('Before:\n```csharp\nvar key = $"{tenant}:{id}";\n  **not bold**\n```\nAfter.')).toEqual([
      { kind: 'paragraph', children: [text('Before:')] },
      { kind: 'code', language: 'csharp', text: 'var key = $"{tenant}:{id}";\n  **not bold**' },
      { kind: 'paragraph', children: [text('After.')] },
    ]);
  });

  it('runs an unclosed fence to the end rather than dropping the text', () => {
    expect(parseMarkdown('~~~\nline one\nline two')).toEqual([
      { kind: 'code', language: '', text: 'line one\nline two' },
    ]);
  });

  it('reads triple backticks on one line as a code span, not a fence', () => {
    expect(parseMarkdown('```inline```')).toEqual([
      { kind: 'paragraph', children: [{ kind: 'code', text: 'inline' }] },
    ]);
  });

  it('keeps a heading as a bold line', () => {
    expect(parseMarkdown('## Why')).toEqual([
      { kind: 'paragraph', children: [{ kind: 'strong', children: [text('Why')] }] },
    ]);
  });

  it('reads a fence inside a list item', () => {
    const list = listOf('- run:\n  ```\n  npm test\n  ```');

    expect(list.items[0]).toEqual([
      { kind: 'paragraph', children: [text('run:')] },
      { kind: 'code', language: '', text: 'npm test' },
    ]);
  });

  it('treats Windows line endings the same as Unix ones', () => {
    expect(parseMarkdown('a\r\n\r\nb')).toEqual(parseMarkdown('a\n\nb'));
  });
});

describe('plainText', () => {
  it('takes the markup away and puts the words on one line', () => {
    expect(plainText('**Bold** and `code`\n- one\n- two\n\n```\nx = 1\n```')).toBe(
      'Bold and code one · two x = 1',
    );
  });

  it('returns plain text unchanged', () => {
    expect(plainText('Cache key now includes the tenant.')).toBe(
      'Cache key now includes the tenant.',
    );
  });
});
