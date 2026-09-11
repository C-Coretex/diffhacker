import { describe, expect, it } from 'vitest';
import { buildElkGraph, bundledEdges, edgesBetween, isSyntheticEdge, type ElkNode } from './elkGraph';
import { ENTRY_NODE_OPTIONS, mergedHeight, NODE_HEIGHT } from './elkOptions';
import { toFlowGraph, type ImplementationGroupNodeData, type ReadingEdgeData } from './flowGraph';
import { mergedBoxId, mergePlan, NO_MERGE } from './implementationGroups';
import { inProcessLayout } from './runLayout';
import { implementationView, node, testView } from './testGraph';

const BOX = mergedBoxId('src/IStore.cs');

/**
 * An abstraction and its implementations drawn as one box.
 *
 * Checked in the ELK input and in the React Flow output rather than by eye, for the same reason the
 * cross-container rule is: "the three files are one box, the box is where the first of them was, and
 * no line the model drew was lost on the way" are all properties of plain objects.
 */
describe('mergePlan', () => {
  it('merges nothing when the reviewer has merging off', () => {
    expect(mergePlan(implementationView(), false)).toBe(NO_MERGE);
  });

  it('draws the abstraction first and its implementations after it', () => {
    const plan = mergePlan(implementationView(), true);

    expect(plan.boxes.get(BOX)?.memberIds).toEqual([
      'src/IStore.cs',
      'src/MemoryStore.cs',
      'src/DiskStore.cs',
    ]);

    expect(plan.boxOf.get('src/DiskStore.cs')).toBe(BOX);
    expect(plan.boxOf.has('src/Caller.cs')).toBe(false);
  });

  it('leaves a member outside the abstraction’s container, or already claimed, as its own box', () => {
    // The host already filters to one container; this is the renderer refusing to draw a node twice
    // or in the wrong cluster if it ever did not.
    const plan = mergePlan(
      implementationView({
        implementationGroups: [
          { abstractionNodeId: 'src/IStore.cs', implementationNodeIds: ['src/MemoryStore.cs', 'src/Notes.md'], containerId: 'store' },
          { abstractionNodeId: 'src/Caller.cs', implementationNodeIds: ['src/MemoryStore.cs'], containerId: 'store' },
        ],
      }),
      true,
    );

    expect(plan.boxes.get(BOX)?.memberIds).toEqual(['src/IStore.cs', 'src/MemoryStore.cs']);
    expect(plan.boxes.size).toBe(1);
    expect(plan.boxOf.has('src/Notes.md')).toBe(false);
  });
});

describe('buildElkGraph with merged boxes', () => {
  it('lays each merged box out as one leaf, where its earliest member stood, sized for its rows', () => {
    const store = childOf(buildElkGraph(implementationView(), new Set(), mergePlan(implementationView(), true)), 'store');

    expect(store.children?.map((child) => child.id)).toEqual([BOX, 'src/Caller.cs']);

    const box = store.children?.find((child) => child.id === BOX);
    expect(box?.height).toBe(mergedHeight(3));
    expect(store.children?.find((child) => child.id === 'src/Caller.cs')?.height).toBe(NODE_HEIGHT);
  });

  it('pins the box to the first layer when one of its members is the entry node', () => {
    const store = childOf(buildElkGraph(implementationView(), new Set(), mergePlan(implementationView(), true)), 'store');

    expect(store.children?.find((child) => child.id === BOX)?.layoutOptions).toMatchObject(ENTRY_NODE_OPTIONS);
  });

  it('drops edges inside a box and folds edges landing on the same pair into one', () => {
    const store = childOf(buildElkGraph(implementationView(), new Set(), mergePlan(implementationView(), true)), 'store');
    const ids = (store.edges ?? []).filter((edge) => !isSyntheticEdge(edge.id)).map((edge) => edge.id);

    // IStore → MemoryStore and IStore → DiskStore are inside the box; each implementation → Caller
    // is the same line out of it.
    expect(ids).toEqual([`${BOX}->src/Caller.cs`]);
  });

  it('is exactly the unmerged graph when nothing is merged', () => {
    const view = implementationView();

    expect(buildElkGraph(view, new Set(), NO_MERGE)).toEqual(buildElkGraph(view, new Set()));
    expect(childOf(buildElkGraph(view, new Set(), NO_MERGE), 'store').children).toHaveLength(4);
  });
});

describe('bundledEdges with merged boxes', () => {
  it('draws a cross-container edge from the box its source was merged into', () => {
    const bundles = bundledEdges(implementationView(), new Set(), mergePlan(implementationView(), true));

    expect(bundles.map((bundle) => bundle.id)).toEqual([`${BOX}->src/Notes.md`]);
  });

  it('lets a collapsed container win over a merged box inside it', () => {
    const bundles = bundledEdges(implementationView(), new Set(['store']), mergePlan(implementationView(), true));

    expect(bundles.map((bundle) => bundle.id)).toEqual(['store->src/Notes.md']);
  });
});

describe('toFlowGraph with merged boxes', () => {
  it('draws one box with a row per node, and every node still on the diagram', async () => {
    const view = implementationView();
    const merge = mergePlan(view, true);
    const graph = toFlowGraph(view, await inProcessLayout(buildElkGraph(view, new Set(), merge)), new Set(), undefined, merge);

    const box = graph.nodes.find((candidate) => candidate.id === BOX);
    expect(box?.type).toBe('implementationGroup');

    const rows = (box?.data as ImplementationGroupNodeData).rows;
    expect(rows.map((row) => row.node.id)).toEqual(['src/IStore.cs', 'src/MemoryStore.cs', 'src/DiskStore.cs']);
    expect(rows[0]?.isEntry).toBe(true);

    // §0.2.5 on screen: a file box for each node not merged, a row for each node that was.
    const drawn = [
      ...graph.nodes.filter((candidate) => candidate.type === 'file').map((candidate) => candidate.id),
      ...rows.map((row) => row.node.id),
    ];
    expect(drawn.sort()).toEqual(view.nodes.map((candidate) => candidate.id).sort());
  });

  it('keeps every model edge a folded line stands for, for its card', async () => {
    const view = implementationView();
    const merge = mergePlan(view, true);
    const graph = toFlowGraph(view, await inProcessLayout(buildElkGraph(view, new Set(), merge)), new Set(), undefined, merge);

    const folded = graph.edges.find((candidate) => candidate.id === `${BOX}->src/Caller.cs`);
    const data = folded?.data as ReadingEdgeData;

    expect(data.count).toBe(2);
    expect(data.kind).toBe('bundle');
    expect(data.edges.map((edge) => edge.sourceNodeId).sort()).toEqual(['src/DiskStore.cs', 'src/MemoryStore.cs']);
  });
});

describe('edgesBetween', () => {
  it('is the one model edge between two ordinary boxes', () => {
    const view = testView();

    expect(edgesBetween(view, 'src/Contract.cs', 'src/Caller.cs')).toHaveLength(1);
  });

  it('never counts a self-edge', () => {
    const view = testView({ nodes: [node('src/Contract.cs', 'core', 1, ['changed'])], edges: [] });

    expect(edgesBetween(view, 'src/Contract.cs', 'src/Contract.cs')).toEqual([]);
  });
});

function childOf(graph: ElkNode, id: string): ElkNode {
  const child = graph.children?.find((candidate) => candidate.id === id);
  if (!child) throw new Error(`No container '${id}' in the ELK input.`);
  return child;
}
