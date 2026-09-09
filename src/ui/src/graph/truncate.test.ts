import { describe, expect, it } from 'vitest';
import { basename, clamp, directoryContext, PATH_BUDGET } from './truncate';

describe('basename', () => {
  it('is never truncated, however long the path in front of it', () => {
    expect(basename('src/DiffHacker.Core/Analyses/AnalysisValidator.cs')).toBe(
      'AnalysisValidator.cs',
    );
  });

  it('returns the whole thing for a file at the repository root', () => {
    expect(basename('README.md')).toBe('README.md');
  });
});

describe('directoryContext', () => {
  it('says nothing for a file at the repository root', () => {
    // A lone slash on the box would be noise pretending to be information.
    expect(directoryContext('README.md')).toBe('');
  });

  it('leaves a short path alone', () => {
    expect(directoryContext('src/App.tsx')).toBe('src/');
  });

  it('elides by segment rather than by character', () => {
    // The point of the rule. "src/DiffHacker.Core/Ana…" tells the reader nothing about where the
    // file is; the first segment plus the last two tells them the area and the neighbourhood.
    const elided = directoryContext('src/DiffHacker.Core/Analyses/Repairs/Rounds/File.cs');

    expect(elided).toBe('src/…/Repairs/Rounds/');
    expect(elided.length).toBeLessThanOrEqual(PATH_BUDGET);
  });

  it('drops to one surviving segment when two still do not fit', () => {
    const path = [
      'src',
      'AVeryLongProjectNameIndeed.Core.Infrastructure',
      'AnotherExtremelyLongDirectoryName',
      'AndOneMoreForGoodMeasureHere',
      'File.cs',
    ].join('/');

    const elided = directoryContext(path);

    expect(elided).toBe('src/…/AndOneMoreForGoodMeasureHere/');
  });

  it('keeps the first and last of a short but wide path', () => {
    const elided = directoryContext(
      'averyveryverylongfirstsegmentindeed/andasecondlongone/File.cs',
    );

    expect(elided).toBe('averyveryverylongfirstsegmentindeed/…/andasecondlongone/');
  });
});

describe('clamp', () => {
  it('leaves text within budget untouched', () => {
    expect(clamp('Evicts on tenant change', 60)).toBe('Evicts on tenant change');
  });

  it('never returns more characters than the budget, ellipsis included', () => {
    // What keeps a fixed-size box fixed: an ellipsis added past the budget would make a truncated
    // string longer than an untruncated one.
    const clamped = clamp('a'.repeat(200), 40);

    expect(clamped.length).toBeLessThanOrEqual(40);
    expect(clamped.endsWith('…')).toBe(true);
  });

  it('breaks at a word when one is close enough to the edge', () => {
    expect(clamp('The cache key now includes the tenant identifier', 30)).toBe(
      'The cache key now includes…',
    );
  });

  it('breaks mid-word rather than collapsing the line to nothing', () => {
    // A single very long word at the start would otherwise leave two characters and an ellipsis.
    expect(clamp('Supercalifragilisticexpialidocious is here', 20)).toBe('Supercalifragilistic'.slice(0, 19) + '…');
  });
});
