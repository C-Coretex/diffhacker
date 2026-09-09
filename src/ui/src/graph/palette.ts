import type { AnalysisView } from '@/contracts';

/**
 * How many distinct project colours `index.css` defines. The eleventh project onward shares the
 * neutral one: past ten, another hue is not another *distinguishable* hue, and a legend of
 * fifteen near-identical swatches is worse than an honest "these are the rest".
 */
export const PROJECT_COLOURS = 10;

/**
 * The order colours are handed out in, rather than 1, 2, 3.
 *
 * Two reasons, both about the eye rather than the code. Projects are sorted by size, so the two
 * biggest would otherwise get adjacent hues — the two blocks of colour covering most of the
 * diagram would be the hardest pair to tell apart. And slots 1 and 4 are the red and the green
 * that a protanope collapses into one another, so they are never the first two given out.
 */
const HANDOUT_ORDER = [0, 4, 8, 2, 6, 1, 5, 9, 3, 7];

/** A project and the colour slot it was given. */
export interface ProjectColour {
  readonly project: string;
  /** 1-10 for a coloured slot, or null for the shared neutral one. */
  readonly slot: number | null;
  /** How many nodes belong to it. What the legend counts and what the sort ranked. */
  readonly nodeCount: number;
}

/**
 * Assigns a colour slot to every project in the analysis.
 *
 * Sorted by node count descending and then by name, so the assignment is *stable*: the same
 * analysis reopened next week gets the same colours, and a reviewer who learned that green is the
 * host does not have to relearn it. Ties broken by name for the same reason — node counts are
 * equal often enough that leaving it to input order would make the palette shuffle on a re-run.
 */
export function assignProjectColours(view: AnalysisView): ProjectColour[] {
  const counts = new Map<string, number>();
  const projectOf = projectsByPath(view);

  for (const node of view.nodes) {
    const project = projectOf.get(node.filePath);
    if (project === undefined || project.length === 0) continue;
    counts.set(project, (counts.get(project) ?? 0) + 1);
  }

  const ordered = [...counts.entries()].sort(
    ([aName, aCount], [bName, bCount]) => bCount - aCount || aName.localeCompare(bName),
  );

  return ordered.map(([project, nodeCount], rank) => ({
    project,
    nodeCount,
    slot: rank < PROJECT_COLOURS ? (HANDOUT_ORDER[rank] ?? rank) + 1 : null,
  }));
}

/** Project name by file path, from the changeset the analysis was made from. */
export function projectsByPath(view: AnalysisView): Map<string, string> {
  return new Map(view.changedFiles.map((file) => [file.path, file.project]));
}

/**
 * The CSS variable names for one slot. Returned as `var(...)` strings rather than Tailwind class
 * names because the slot is chosen at runtime: a class built by string concatenation is one
 * Tailwind's scanner cannot see and therefore never emits.
 */
export function projectColourStyle(slot: number | null): {
  readonly fill: string;
  readonly rail: string;
} {
  return slot === null
    ? { fill: 'var(--project-other)', rail: 'var(--project-other-rail)' }
    : { fill: `var(--project-${slot})`, rail: `var(--project-${slot}-rail)` };
}

/** Slot by project name, for the node components. */
export function colourSlots(colours: readonly ProjectColour[]): Map<string, number | null> {
  return new Map(colours.map((entry) => [entry.project, entry.slot]));
}
