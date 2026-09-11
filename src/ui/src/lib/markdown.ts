/**
 * The small Markdown the model writes its prose in, parsed into a tree the renderer draws.
 *
 * **A subset we chose, not CommonMark.** The analysis prompt tells the model exactly what it may
 * use — bold, italic, inline code, bullet and numbered lists, fenced code blocks — and this file
 * understands that and a little more it is likely to reach for anyway. Anything else is left as the
 * literal text it was: a table stays a row of pipes, a heading loses its hashes and keeps its words.
 * Because we name the grammar in the prompt, it only has to be complete for the grammar we named. A
 * general parser package would add forty transitive dependencies to render less than this.
 *
 * **The output is data, never HTML.** Nothing here returns a string that anything writes into the
 * DOM, so there is no sanitiser, because nothing needs sanitising: `<script>` in a summary is eight
 * characters of text that React escapes. Links are parsed only so their brackets disappear.
 * `Markdown.tsx` draws the link's text and never an `href`, because a click that navigated the
 * WebView away from `diffhacker://app` would leave the reviewer with no application (§0.2.13).
 *
 * **Every newline is a line break**, as in a GitHub comment and unlike CommonMark. Plain-text
 * analyses were stored before the model was asked for Markdown, and they were written to be shown
 * with `white-space: pre-wrap`. Keeping their line breaks lets them render as they always did.
 */

export type Inline =
  | { readonly kind: 'text'; readonly text: string }
  | { readonly kind: 'strong'; readonly children: readonly Inline[] }
  | { readonly kind: 'emphasis'; readonly children: readonly Inline[] }
  | { readonly kind: 'code'; readonly text: string }
  /** `[text](target)`. The target is kept so a path the analysis knows can become a reference. */
  | { readonly kind: 'link'; readonly target: string; readonly children: readonly Inline[] }
  | { readonly kind: 'break' };

export type Block =
  | { readonly kind: 'paragraph'; readonly children: readonly Inline[] }
  | {
      readonly kind: 'list';
      readonly ordered: boolean;
      readonly start: number;
      readonly items: readonly (readonly Block[])[];
    }
  | { readonly kind: 'code'; readonly language: string; readonly text: string };

/** The whole grammar: paragraphs, lists and fenced code, with inline formatting inside them. */
export function parseMarkdown(text: string): Block[] {
  return parseBlocks(text.replace(/\r\n?/g, '\n').split('\n').map(expandLeadingTabs));
}

/**
 * Inline formatting only. For a risk, which is one line item in a column of them and must never
 * turn into a list or a code block: a structure there would be a paragraph inside the risk column.
 */
export function parseInline(text: string): Inline[] {
  const normalised = text.replace(/\r\n?/g, '\n');
  return parseSpan(normalised, 0, normalised.length);
}

/**
 * What the text says with the markup taken away, on one line.
 *
 * For every place that shows prose as a clamped preview or a tooltip rather than drawing it. A
 * preview clamped at 160 characters would otherwise end in the middle of a `**` and show the reader
 * a stray pair of asterisks.
 */
export function plainText(text: string): string {
  return parseMarkdown(text).map(blockText).join(' ').replace(/\s+/g, ' ').trim();
}

// ---------------------------------------------------------------------------------------------
// Blocks

const FENCE_OPEN = /^( {0,3})(`{3,}|~{3,})[ \t]*([^`\s]*)[^`]*$/;
const FENCE_CLOSE = /^ {0,3}(`{3,}|~{3,})[ \t]*$/;
const LIST_ITEM = /^( {0,3})([-*+]|\d{1,9}[.)])( +|$)(.*)$/;
const HEADING = /^ {0,3}#{1,6}[ \t]+(.*?)[ \t#]*$/;

function parseBlocks(lines: readonly string[]): Block[] {
  const blocks: Block[] = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i] ?? '';

    if (isBlank(line)) {
      i++;
      continue;
    }

    const fence = FENCE_OPEN.exec(line);
    if (fence) {
      i = readFence(lines, i, fence, blocks);
      continue;
    }

    if (LIST_ITEM.test(line)) {
      i = readList(lines, i, blocks);
      continue;
    }

    // The prompt asks for no headings, because a card has no room for one. When one arrives
    // anyway its words are kept and it reads as what it is: a line that matters more than the rest.
    const heading = HEADING.exec(line);
    if (heading) {
      blocks.push({
        kind: 'paragraph',
        children: [{ kind: 'strong', children: parseInline(heading[1] ?? '') }],
      });
      i++;
      continue;
    }

    const start = i;
    i++;
    while (i < lines.length && !endsParagraph(lines[i] ?? '')) i++;

    blocks.push({
      kind: 'paragraph',
      children: parseInline(
        lines
          .slice(start, i)
          .map((l) => l.trim())
          .join('\n'),
      ),
    });
  }

  return blocks;
}

/**
 * Whether a line stops the paragraph above it.
 *
 * A bulleted list may start straight after a sentence with no blank line between them, because
 * that is how a model writes "Three callers changed:" followed by the three. A numbered list may
 * only do so from 1, as in CommonMark, so a sentence wrapped just before "2024." stays a sentence.
 */
function endsParagraph(line: string): boolean {
  if (isBlank(line) || FENCE_OPEN.test(line) || HEADING.test(line)) return true;

  const item = LIST_ITEM.exec(line);
  return item !== null && (!isOrdered(item) || Number.parseInt(item[2] ?? '', 10) === 1);
}

function readFence(
  lines: readonly string[],
  open: number,
  fence: RegExpExecArray,
  blocks: Block[],
): number {
  const indent = (fence[1] ?? '').length;
  const marker = fence[2] ?? '```';
  const content: string[] = [];
  let i = open + 1;

  for (; i < lines.length; i++) {
    const line = lines[i] ?? '';
    const close = FENCE_CLOSE.exec(line)?.[1];
    if (close && close[0] === marker[0] && close.length >= marker.length) {
      i++;
      break;
    }

    // An indented fence indents its content by as much, and that indent is not part of the code.
    content.push(line.replace(new RegExp(`^ {0,${indent}}`), ''));
  }

  // An unclosed fence runs to the end, as CommonMark has it. Dropping the text instead would lose
  // the model's content over a missing line of backticks.
  blocks.push({ kind: 'code', language: fence[3] ?? '', text: content.join('\n') });
  return i;
}

function readList(lines: readonly string[], first: number, blocks: Block[]): number {
  const head = LIST_ITEM.exec(lines[first] ?? '')!;
  const ordered = isOrdered(head);
  const start = ordered ? Number.parseInt(head[2] ?? '', 10) : 1;

  const items: string[][] = [];
  let current: string[] = [];
  let contentIndent = 0;
  let i = first;

  while (i < lines.length) {
    const line = lines[i] ?? '';
    const item = LIST_ITEM.exec(line);

    // A marker less indented than the current item's text is a sibling. One indented as far as the
    // text or further belongs inside the current item, where it becomes a nested list.
    if (item && (items.length === 0 || indentOf(line) < contentIndent)) {
      if (isOrdered(item) !== ordered) break;

      const [, indent = '', marker = '', gap = '', content = ''] = item;
      current = [content];
      items.push(current);
      contentIndent = indent.length + marker.length + Math.min(Math.max(gap.length, 1), 4);
      i++;
      continue;
    }

    if (isBlank(line)) {
      let next = i + 1;
      while (next < lines.length && isBlank(lines[next] ?? '')) next++;
      if (next >= lines.length) break;

      const following = lines[next] ?? '';
      const sibling = LIST_ITEM.exec(following);
      const continues =
        indentOf(following) >= contentIndent ||
        (sibling !== null && isOrdered(sibling) === ordered && indentOf(following) < contentIndent);

      if (!continues) break;

      for (; i < next; i++) current.push('');
      continue;
    }

    if (indentOf(line) >= contentIndent) {
      current.push(line.slice(contentIndent));
      i++;
      continue;
    }

    // Neither a marker nor indented: a wrapped line of the item's text, unless it starts a block of
    // its own. A model that wraps a long bullet without indenting the rest means the same bullet.
    if (item || FENCE_OPEN.test(line) || HEADING.test(line)) break;

    current.push(line.trim());
    i++;
  }

  blocks.push({ kind: 'list', ordered, start, items: items.map((body) => parseBlocks(body)) });
  return i;
}

function isOrdered(item: RegExpExecArray): boolean {
  return /^\d/.test(item[2] ?? '');
}

function isBlank(line: string): boolean {
  return line.trim().length === 0;
}

function indentOf(line: string): number {
  return line.length - line.trimStart().length;
}

function expandLeadingTabs(line: string): string {
  return line.replace(/^[ \t]+/, (indent) => indent.replace(/\t/g, '    '));
}

// ---------------------------------------------------------------------------------------------
// Inline

const PUNCTUATION = /[!-/:-@[-`{-~]/;

/** Parses `text[from, to)` into inline nodes. Recursive for what a delimiter pair encloses. */
function parseSpan(text: string, from: number, to: number): Inline[] {
  const out: Inline[] = [];
  let buffer = '';

  const flush = () => {
    if (buffer) out.push({ kind: 'text', text: buffer });
    buffer = '';
  };

  let i = from;
  while (i < to) {
    const ch = text[i];

    const escaped = text[i + 1] ?? '';
    if (ch === '\\' && i + 1 < to && PUNCTUATION.test(escaped)) {
      buffer += escaped;
      i += 2;
      continue;
    }

    if (ch === '\n') {
      flush();
      out.push({ kind: 'break' });
      i++;
      continue;
    }

    if (ch === '`') {
      const run = runLength(text, i, to, '`');
      const close = findCodeClose(text, i + run, to, run);

      if (close < 0) {
        buffer += text.slice(i, i + run);
        i += run;
        continue;
      }

      flush();
      out.push({ kind: 'code', text: codeSpanText(text.slice(i + run, close)) });
      i = close + run;
      continue;
    }

    // `*` only. `_` is left as text, because what a model writes with underscores in it is
    // `snake_case`, `__init__.py` and `_private` far more often than it is emphasis, and the prompt
    // asks for asterisks.
    if (ch === '*') {
      const run = runLength(text, i, to, '*');
      const size = run <= 3 ? run : 0;
      const close = size > 0 && canOpen(text, i + run, to) ? findEmphasisClose(text, i + size, to, size) : -1;

      if (close < 0) {
        buffer += text.slice(i, i + run);
        i += run;
        continue;
      }

      flush();
      const children = parseSpan(text, i + size, close);
      out.push(
        size === 1
          ? { kind: 'emphasis', children }
          : size === 2
            ? { kind: 'strong', children }
            : { kind: 'strong', children: [{ kind: 'emphasis', children }] },
      );
      i = close + size;
      continue;
    }

    if (ch === '[' || (ch === '!' && text[i + 1] === '[')) {
      const link = readLink(text, ch === '!' ? i + 1 : i, to);

      if (link) {
        flush();
        out.push({
          kind: 'link',
          target: link.target,
          children: parseSpan(text, link.textFrom, link.textTo),
        });
        i = link.end;
        continue;
      }
    }

    buffer += ch;
    i++;
  }

  flush();
  return out;
}

function runLength(text: string, at: number, to: number, ch: string): number {
  let end = at;
  while (end < to && text[end] === ch) end++;
  return end - at;
}

/** The opening of a closing backtick run of exactly `run`, or -1. */
function findCodeClose(text: string, from: number, to: number, run: number): number {
  let j = from;

  while (j < to) {
    if (text[j] !== '`') {
      j++;
      continue;
    }

    const length = runLength(text, j, to, '`');
    if (length === run) return j;
    j += length;
  }

  return -1;
}

/** CommonMark's rule: one leading and one trailing space are padding when both are there. */
function codeSpanText(raw: string): string {
  const text = raw.replace(/\n/g, ' ');
  return text.length > 2 && text.startsWith(' ') && text.endsWith(' ') && text.trim().length > 0
    ? text.slice(1, -1)
    : text;
}

/**
 * Whether the delimiter run ending at `after` can open emphasis: only when something other than a
 * space follows it, so `a * b * c` stays arithmetic.
 */
function canOpen(text: string, after: number, to: number): boolean {
  return after < to && !/\s/.test(text[after] ?? ' ');
}

/**
 * The start of the run that closes emphasis opened by `size` asterisks, or -1.
 *
 * A closing run must be exactly as long as the opening one, so `**bold *and italic* bold**` pairs
 * each delimiter with its own, and it must follow something other than a space. Code spans are
 * skipped whole, because a `*` inside backticks is code and never closes anything outside them.
 */
function findEmphasisClose(text: string, from: number, to: number, size: number): number {
  let j = from;

  while (j < to) {
    const c = text[j];

    if (c === '\\') {
      j += 2;
      continue;
    }

    if (c === '`') {
      const run = runLength(text, j, to, '`');
      const close = findCodeClose(text, j + run, to, run);
      j = close < 0 ? j + run : close + run;
      continue;
    }

    if (c !== '*') {
      j++;
      continue;
    }

    const run = runLength(text, j, to, '*');
    if (run === size && j > from && !/\s/.test(text[j - 1] ?? ' ')) return j;
    j += run;
  }

  return -1;
}

/** `[text](target)`, with the positions of the text and where the whole thing ends. */
function readLink(
  text: string,
  open: number,
  to: number,
): { textFrom: number; textTo: number; target: string; end: number } | undefined {
  const closeBracket = text.indexOf(']', open + 1);
  if (closeBracket < 0 || closeBracket + 1 >= to || text[closeBracket + 1] !== '(') return undefined;

  const closeParen = text.indexOf(')', closeBracket + 2);
  if (closeParen < 0 || closeParen >= to) return undefined;

  const inside = text.slice(open + 1, closeBracket);
  if (inside.includes('\n')) return undefined;

  // The target only, without the optional "title" CommonMark allows after it.
  const target = text
    .slice(closeBracket + 2, closeParen)
    .trim()
    .replace(/^<(.*)>$/, '$1')
    .split(/\s+/)[0] ?? '';

  return { textFrom: open + 1, textTo: closeBracket, target, end: closeParen + 1 };
}

// ---------------------------------------------------------------------------------------------
// Plain text

function blockText(block: Block): string {
  switch (block.kind) {
    case 'paragraph':
      return inlineText(block.children);
    case 'code':
      return block.text;
    case 'list':
      return block.items.map((item) => item.map(blockText).join(' ')).join(' · ');
  }
}

function inlineText(nodes: readonly Inline[]): string {
  return nodes
    .map((node) => {
      switch (node.kind) {
        case 'text':
        case 'code':
          return node.text;
        case 'break':
          return ' ';
        default:
          return inlineText(node.children);
      }
    })
    .join('');
}
