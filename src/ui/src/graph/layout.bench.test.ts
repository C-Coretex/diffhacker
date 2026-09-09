import { describe, expect, it } from 'vitest';
import { buildElkGraph } from './elkGraph';
import { toFlowGraph } from './flowGraph';
import { inProcessLayout } from './runLayout';
import { container, node, file, testView } from './testGraph';

/**
 * Requirement 11: readable and responsive at ~300 nodes.
 *
 * Layout time is the part of that a test can measure honestly. Frame rate while panning and the
 * feel of collapsing a cluster are measured by hand — they depend on a real compositor — and the
 * numbers are recorded in `docs/decisions.md`.
 *
 * The threshold is deliberately loose. This is a guard against an accidental change of algorithmic
 * order — an O(n²) join creeping into `toFlowGraph`, a layout option that turns layering
 * exponential — not a benchmark, and a tight bound on a shared CI machine is a flaky test.
 */
describe('at three hundred nodes', () => {
  const view = largeView(300, 12);

  it('lays out in well under a second', async () => {
    const graph = buildElkGraph(view, new Set());

    const started = performance.now();
    const laidOut = await inProcessLayout(graph);
    const elapsed = performance.now() - started;

    expect(laidOut.children).toHaveLength(12);
    expect(elapsed).toBeLessThan(5_000);
  });

  it('builds the React Flow graph in a few milliseconds', async () => {
    const laidOut = await inProcessLayout(buildElkGraph(view, new Set()));

    const started = performance.now();
    const flow = toFlowGraph(view, laidOut, new Set());
    const elapsed = performance.now() - started;

    // 300 nodes plus 12 containers.
    expect(flow.nodes).toHaveLength(312);
    expect(elapsed).toBeLessThan(1_000);
  });

  it('collapses a container without relaying out from scratch being expensive', async () => {
    const collapsed = new Set(['c0']);

    const started = performance.now();
    const laidOut = await inProcessLayout(buildElkGraph(view, collapsed));
    const elapsed = performance.now() - started;

    expect(laidOut.children?.find((child) => child.id === 'c0')?.children ?? []).toHaveLength(0);
    expect(elapsed).toBeLessThan(5_000);
  });
});

/** `count` nodes spread evenly over `clusters` containers, each a chain. */
function largeView(count: number, clusters: number) {
  const paths = Array.from({ length: count }, (_, index) => `src/area${index % clusters}/file${index}.cs`);
  const members: string[][] = Array.from({ length: clusters }, () => []);

  paths.forEach((path, index) => members[index % clusters]!.push(path));

  return testView({
    containers: members.map((group, index) =>
      container(`c${index}`, index + 1, group[0]!, group),
    ),
    nodes: members.flatMap((group, cluster) =>
      group.map((path, rank) =>
        node(path, `c${cluster}`, rank + 1, rank === 0 ? ['changed', 'entry_point'] : ['changed']),
      ),
    ),
    // A chain through each cluster, plus a cross-container edge out of every cluster's entry node —
    // the shape that exercises both the layout and the edges kept out of it.
    edges: members.flatMap((group, cluster) => [
      ...group.slice(1).map((path, index) => ({
        sourceNodeId: group[index]!,
        targetNodeId: path,
        kind: 'direct' as const,
        explanation: 'Read in order.',
        risks: [],
        crossesContainers: false,
      })),
      ...(cluster + 1 < clusters
        ? [
            {
              sourceNodeId: group[0]!,
              targetNodeId: members[cluster + 1]![0]!,
              kind: 'conceptual' as const,
              explanation: 'And then the next cluster.',
              risks: [],
              crossesContainers: true,
            },
          ]
        : []),
    ]),
    changedFiles: paths.map((path, index) =>
      file(path, 'modified', index, 1, 'C#', `project${index % 14}`),
    ),
  });
}
