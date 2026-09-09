import { describe, expect, it } from 'vitest';
import { buildElkGraph, bundledEdges, isSyntheticEdge, type ElkNode } from './elkGraph';
import { ENTRY_NODE_OPTIONS } from './elkOptions';
import { testView, twoContainerView, container, node, edge } from './testGraph';

/**
 * What ELK is asked to lay out, checked by reading it.
 *
 * Verification step 6 of the iteration asks for cross-container edges to be confirmed excluded from
 * the layout "in the ELK input, not by eye", and step 2 asks for the entry node to be at the top of
 * every container. Both are properties of this object, so both are assertions rather than
 * screenshots — which is why the input is built by a pure function that never touches a DOM.
 */
describe('buildElkGraph', () => {
  it('gives every container its own sub-graph and every node a fixed size', () => {
    const graph = buildElkGraph(twoContainerView(), new Set());

    expect(graph.children?.map((child) => child.id)).toEqual(['core', 'docs']);

    const core = childOf(graph, 'core');
    expect(core.children?.map((child) => child.id)).toEqual(['src/Contract.cs', 'src/Caller.cs']);

    for (const child of core.children ?? []) {
      expect(child.width).toBeGreaterThan(0);
      expect(child.height).toBeGreaterThan(0);
    }
  });

  it('pins the entry node to the first layer', () => {
    // Requirement 3 and verification step 2. Without the constraint, an entry node with an incoming
    // edge from one of its own consequences would be placed below it — exactly backwards.
    const graph = buildElkGraph(twoContainerView(), new Set());
    const core = childOf(graph, 'core');

    const entry = core.children?.find((child) => child.id === 'src/Contract.cs');
    expect(entry?.layoutOptions).toMatchObject(ENTRY_NODE_OPTIONS);

    const follower = core.children?.find((child) => child.id === 'src/Caller.cs');
    expect(follower?.layoutOptions).toBeUndefined();
  });

  it('emits children in the rank order the model gave, not the order the array happened to be in', () => {
    const view = testView({
      containers: [container('core', 1, 'b.cs', ['a.cs', 'b.cs', 'c.cs'])],
      nodes: [
        node('a.cs', 'core', 3, ['changed']),
        node('b.cs', 'core', 1, ['changed', 'entry_point']),
        node('c.cs', 'core', 2, ['changed']),
      ],
      edges: [],
      changedFiles: [],
    });

    const core = childOf(buildElkGraph(view, new Set()), 'core');

    expect(core.children?.map((child) => child.id)).toEqual(['b.cs', 'c.cs', 'a.cs']);
  });

  it('excludes every cross-container edge from the layout', () => {
    // Requirement 7, checkable in the input. The two conceptual edges in this fixture go from the
    // core cluster to the docs one and must appear in neither sub-graph.
    const graph = buildElkGraph(twoContainerView(), new Set());

    const everyEdgeId = (graph.children ?? []).flatMap((child) =>
      (child.edges ?? []).map((elkEdge) => elkEdge.id),
    );

    expect(everyEdgeId).not.toContain('src/Contract.cs->src/Notes.md');
    expect(everyEdgeId).not.toContain('src/Caller.cs->src/Notes.md');
    expect(everyEdgeId).toContain('src/Contract.cs->src/Caller.cs');

    // And nothing at the root either — the root only packs containers.
    expect(graph.edges ?? []).toEqual([]);
  });

  it('chains a ranked node that nothing points at, and marks the chain synthetic', () => {
    // src/Notes.md is rank 3 with no incoming edge. Without a synthetic edge ELK would place it in
    // the first layer beside the entry node, and the diagram would show two starting points.
    const core = childOf(buildElkGraph(testView(), new Set()), 'core');
    const synthetic = (core.edges ?? []).filter((elkEdge) => isSyntheticEdge(elkEdge.id));

    expect(synthetic).toHaveLength(1);
    expect(synthetic[0]?.sources).toEqual(['src/Caller.cs']);
    expect(synthetic[0]?.targets).toEqual(['src/Notes.md']);
  });

  it('does not chain a node an edge already places', () => {
    const core = childOf(buildElkGraph(twoContainerView(), new Set()), 'core');

    expect((core.edges ?? []).filter((elkEdge) => isSyntheticEdge(elkEdge.id))).toHaveLength(0);
  });

  it('drops a self-edge rather than handing one to a layered algorithm', () => {
    const view = testView({ edges: [edge('src/Contract.cs', 'src/Contract.cs', 'conceptual')] });
    const core = childOf(buildElkGraph(view, new Set()), 'core');

    expect((core.edges ?? []).map((elkEdge) => elkEdge.id)).not.toContain(
      'src/Contract.cs->src/Contract.cs',
    );
  });

  it('reduces a collapsed container to one sized leaf with no children and no edges', () => {
    const graph = buildElkGraph(twoContainerView(), new Set(['core']));

    const core = childOf(graph, 'core');
    expect(core.children).toBeUndefined();
    expect(core.edges).toBeUndefined();
    expect(core.width).toBeGreaterThan(0);

    // The other cluster is untouched.
    expect(childOf(graph, 'docs').children).toHaveLength(1);
  });
});

describe('bundledEdges', () => {
  it('carries the cross-container edges React Flow has to draw itself', () => {
    const bundles = bundledEdges(twoContainerView(), new Set());

    expect(bundles.map((bundle) => bundle.id).sort()).toEqual([
      'src/Caller.cs->src/Notes.md',
      'src/Contract.cs->src/Notes.md',
    ]);
    expect(bundles.every((bundle) => bundle.count === 1)).toBe(true);
  });

  it('merges edges that collapse onto the same pair into one, and refuses to call it a kind', () => {
    // Both cross-container edges now end at the collapsed docs box. Two lines between the same two
    // shapes is noise; one line labelled ×2 is the same information. It is neither direct nor
    // conceptual, and claiming either would be inventing a fact.
    const bundles = bundledEdges(twoContainerView(), new Set(['docs']));

    expect(bundles).toHaveLength(2);

    const merged = bundles.filter((bundle) => bundle.target === 'docs');
    expect(merged).toHaveLength(2);
    expect(merged.map((bundle) => bundle.source).sort()).toEqual([
      'src/Caller.cs',
      'src/Contract.cs',
    ]);
  });

  it('folds several edges into one when both ends collapse to the same pair of boxes', () => {
    const bundles = bundledEdges(twoContainerView(), new Set(['core', 'docs']));

    expect(bundles).toHaveLength(1);
    expect(bundles[0]?.source).toBe('core');
    expect(bundles[0]?.target).toBe('docs');
    expect(bundles[0]?.count).toBe(2);
    expect(bundles[0]?.kind).toBeNull();
  });

  it('drops an edge that is wholly inside a collapsed container', () => {
    // Contract → Caller lives entirely in core. Once core is one box, so are both of its ends.
    const bundles = bundledEdges(twoContainerView(), new Set(['core']));

    expect(bundles.some((bundle) => bundle.source === bundle.target)).toBe(false);
    expect(bundles.every((bundle) => bundle.source === 'core')).toBe(true);
  });
});

function childOf(graph: ElkNode, id: string): ElkNode {
  const child = graph.children?.find((candidate) => candidate.id === id);
  if (!child) throw new Error(`No container '${id}' in the ELK input.`);
  return child;
}
