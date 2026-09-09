import { describe, expect, it } from 'vitest';
import { buildElkGraph, type ElkNode } from './elkGraph';
import { inProcessLayout } from './runLayout';
import { twoContainerView } from './testGraph';

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
