import { describe, expect, it } from 'vitest';
import { assignProjectColours, colourSlots, projectColourStyle, PROJECT_COLOURS } from './palette';
import { testView, file, node, container } from './testGraph';

/**
 * Fill colour encodes project (requirement 6), and the number of projects is unbounded — so what
 * this file pins is mostly what happens when there are too many, and that the answer never moves
 * around between runs.
 */
describe('assignProjectColours', () => {
  it('sorts by node count so the biggest project gets the first colour', () => {
    const colours = assignProjectColours(testView());

    expect(colours.map((entry) => entry.project)).toEqual(['DiffHacker.Core', 'docs']);
    expect(colours[0]?.nodeCount).toBe(2);
    expect(colours[1]?.nodeCount).toBe(1);
  });

  it('breaks ties by name, so reopening an analysis gives the same colours', () => {
    // Two projects with one node each. Without the name tiebreak the order would follow whatever
    // the nodes happened to be in, and a reviewer who learned that green means "host" would have to
    // relearn it on every re-run.
    const view = viewWithProjects(['zebra', 'alpha']);

    expect(assignProjectColours(view).map((entry) => entry.project)).toEqual(['alpha', 'zebra']);
  });

  it('hands colours out interleaved rather than in order', () => {
    // The two largest projects must not get adjacent hues: they cover most of the diagram and are
    // the pair most worth telling apart. Slots 1 and 5 are far apart on the wheel; 1 and 2 are not.
    const colours = assignProjectColours(viewWithProjects(['a', 'b', 'c']));

    expect(colours.map((entry) => entry.slot)).toEqual([1, 5, 9]);
  });

  it('gives the eleventh project onward the shared neutral colour', () => {
    const names = Array.from({ length: PROJECT_COLOURS + 3 }, (_, index) =>
      `p${String(index).padStart(2, '0')}`,
    );

    const colours = assignProjectColours(viewWithProjects(names));

    expect(colours.filter((entry) => entry.slot !== null)).toHaveLength(PROJECT_COLOURS);
    expect(colours.filter((entry) => entry.slot === null)).toHaveLength(3);

    // Every coloured slot is used exactly once — no project silently shares another's hue.
    const used = colours.map((entry) => entry.slot).filter((slot): slot is number => slot !== null);
    expect(new Set(used).size).toBe(PROJECT_COLOURS);
  });

  it('ignores a file with no project rather than inventing a category for it', () => {
    const view = testView({
      changedFiles: [
        file('src/Contract.cs', 'modified', 1, 0, 'C#', ''),
        file('src/Caller.cs', 'modified', 1, 0, 'C#', 'DiffHacker.Core'),
        file('src/Notes.md', 'added', 1, 0, 'Markdown', 'DiffHacker.Core'),
      ],
    });

    expect(assignProjectColours(view).map((entry) => entry.project)).toEqual(['DiffHacker.Core']);
  });

  it('yields nothing at all for an analysis stored before per-file facts existed', () => {
    // A schema 1.7 analysis reads back with no changedFiles. The legend is then empty and every box
    // is neutral, which is the honest answer — not a crash, and not a made-up project.
    expect(assignProjectColours(testView({ changedFiles: [] }))).toEqual([]);
  });
});

describe('projectColourStyle', () => {
  it('names the CSS variables rather than a Tailwind class', () => {
    // The slot is chosen at runtime, and a class name built by concatenation is one Tailwind's
    // scanner never sees and therefore never emits.
    expect(projectColourStyle(3)).toEqual({
      fill: 'var(--project-3)',
      rail: 'var(--project-3-rail)',
    });

    expect(projectColourStyle(null)).toEqual({
      fill: 'var(--project-other)',
      rail: 'var(--project-other-rail)',
    });
  });
});

describe('colourSlots', () => {
  it('indexes the assignment by project name', () => {
    const slots = colourSlots(assignProjectColours(testView()));

    expect(slots.get('DiffHacker.Core')).toBe(1);
    expect(slots.get('nothing named this')).toBeUndefined();
  });
});

function viewWithProjects(projects: readonly string[]) {
  const paths = projects.map((_, index) => `src/file${index}.cs`);

  return testView({
    containers: [container('core', 1, paths[0]!, [...paths])],
    nodes: paths.map((path, index) => node(path, 'core', index + 1, ['changed'])),
    edges: [],
    changedFiles: projects.map((project, index) =>
      file(paths[index]!, 'modified', 1, 0, 'C#', project),
    ),
  });
}
