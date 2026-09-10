import { describe, expect, it } from 'vitest';
import { buildElkGraph, type ElkNode } from './elkGraph';
import { inProcessLayout } from './runLayout';
import { groupedViews, twoContainerView } from './testGraph';

/**
 * Requirement 12: the layout output for a fixed input graph, snapshotted, so a layout regression is
 * visible rather than something a reviewer notices weeks later.
 *
 * Run **in process** rather than in the worker. `runLayout.ts` exists as a seam for exactly this:
 * the coordinates ELK produces do not depend on which thread produced them, and a Web Worker in
 * jsdom is a fight with the test environment rather than a test of the layout.
 *
 * The snapshot is of *positions and sizes only*. Bend points and ELK's internal bookkeeping are
 * dropped, because those change between ELK versions without the diagram changing — and a snapshot
 * that fails on a dependency bump is one people learn to update without reading.
 *
 * **To see it work** (verification step 9): change a constant in `elkOptions.ts` — the node width,
 * the spacing, the direction — and this fails.
 */
describe('ELK layout', () => {
  it('places a fixed graph the same way every time', async () => {
    const view = twoContainerView();
    const laidOut = await inProcessLayout(buildElkGraph(view, new Set()));

    expect(positions(laidOut)).toMatchSnapshot();
  });

  it('places the entry node above every other node in its container', async () => {
    // Verification step 2, as an assertion rather than as a look at a picture. The FIRST layer
    // constraint is what makes this true; remove it from elkOptions.ts and this fails.
    const laidOut = await inProcessLayout(buildElkGraph(twoContainerView(), new Set()));

    const core = laidOut.children?.find((child) => child.id === 'core');
    const entry = core?.children?.find((child) => child.id === 'src/Contract.cs');
    const others = (core?.children ?? []).filter((child) => child.id !== 'src/Contract.cs');

    expect(entry).toBeDefined();
    expect(others.length).toBeGreaterThan(0);

    for (const other of others) {
      expect(entry?.y ?? 0).toBeLessThan(other.y ?? 0);
    }
  });

  it('lays the other grouping out as its own diagram', async () => {
    // The two groupings are two arrangements of one node set, so they are two layouts. Snapshotted
    // separately because a change that only moved the change-clusters picture would otherwise be
    // invisible here — and because seeing the two side by side is the clearest statement of what
    // switching actually does.
    const { changeClusters } = groupedViews();
    const laidOut = await inProcessLayout(buildElkGraph(changeClusters, new Set()));

    expect(positions(laidOut)).toMatchSnapshot();
  });

  it('keeps the same node boxes in both groupings', async () => {
    // §0.2.5, measured on the thing that draws the diagram: every node reaches the layout in both
    // pictures, at the same size, in a different place.
    const { dependencyFlow, changeClusters } = groupedViews();

    const flow = await inProcessLayout(buildElkGraph(dependencyFlow, new Set()));
    const clusters = await inProcessLayout(buildElkGraph(changeClusters, new Set()));

    expect(leafIds(clusters)).toEqual(leafIds(flow));

    // One container against three, or there would be nothing to switch to.
    expect(flow.children?.length).toBe(1);
    expect(clusters.children?.length).toBe(3);
  });

  it('lays a collapsed container out as one box', async () => {
    const laidOut = await inProcessLayout(buildElkGraph(twoContainerView(), new Set(['core'])));

    const core = laidOut.children?.find((child) => child.id === 'core');
    expect(core?.children ?? []).toHaveLength(0);
    expect(positions(laidOut)).toMatchSnapshot();
  });
});

/** Ids, positions and sizes — the parts of the answer the diagram actually uses. */
function positions(graph: ElkNode): unknown {
  return {
    id: graph.id,
    x: round(graph.x),
    y: round(graph.y),
    width: round(graph.width),
    height: round(graph.height),
    children: (graph.children ?? []).map(positions),
  };
}

/**
 * Rounded to whole pixels. ELK does its arithmetic in doubles and the last digit of a coordinate is
 * not a layout decision anyone made; a snapshot pinned to it would fail on a different machine's
 * floating point and teach nobody anything.
 */
function round(value: number | undefined): number | undefined {
  return value === undefined ? undefined : Math.round(value);
}

/** Every node box in the laid-out graph, sorted, so two groupings can be compared by node set. */
function leafIds(graph: ElkNode): string[] {
  const ids = (graph.children ?? []).flatMap((child) =>
    (child.children ?? []).length > 0 ? leafIds(child) : [child.id],
  );

  return ids.sort((left, right) => left.localeCompare(right));
}
