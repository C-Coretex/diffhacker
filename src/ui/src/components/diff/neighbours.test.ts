import { describe, expect, it } from 'vitest';
import { fanInFanOutView, testView } from '@/graph/testGraph';
import { neighboursOf, readingPosition } from './neighbours';

describe('neighboursOf', () => {
  it('offers every predecessor and every successor rather than a single next', () => {
    // Requirement 5's whole reason for existing: "graph traversal is not linear — several nodes can
    // point at one node — so 'next' cannot be a single arrow".
    const { predecessors, successors } = neighboursOf(fanInFanOutView(), 'src/Hub.cs');

    expect(predecessors.map((neighbour) => neighbour.node.id)).toEqual([
      'src/A.cs',
      'src/B.cs',
      'src/C.cs',
    ]);

    expect(successors.map((neighbour) => neighbour.node.id)).toEqual([
      'src/Down1.cs',
      'src/Down2.cs',
    ]);
  });

  it('carries what the label needs to say where each choice goes', () => {
    const { predecessors, successors } = neighboursOf(fanInFanOutView(), 'src/Hub.cs');

    // The model's own sentence about the relationship. Without it the button is a filename, which
    // is the file list this product exists to replace.
    expect(predecessors[0]?.explanation).toBe('Read src/Hub.cs after src/A.cs.');

    // Direct and conceptual are visually distinguishable (§0.2.6), so the kind has to reach the label.
    expect(predecessors[0]?.kind).toBe('direct');
    expect(predecessors[2]?.kind).toBe('conceptual');

    // Only the one that leaves the cluster names a cluster; saying "in Cluster core" on every button
    // inside Cluster core would be noise on five out of five.
    expect(successors[0]?.containerTitle).toBeNull();
    expect(successors[1]?.containerTitle).toBe('Cluster docs');
  });

  it('orders the choice by the recommended reading order rather than by edge declaration', () => {
    const view = fanInFanOutView();
    const reversed = {
      ...view,
      edges: [...view.edges].reverse(),
    };

    expect(neighboursOf(reversed, 'src/Hub.cs').predecessors.map((n) => n.node.id)).toEqual([
      'src/A.cs',
      'src/B.cs',
      'src/C.cs',
    ]);
  });

  it('counts a node reached by two edges once', () => {
    const view = testView();
    const twice = {
      ...view,
      edges: [...view.edges, ...view.edges],
    };

    expect(neighboursOf(twice, 'src/Caller.cs').predecessors).toHaveLength(1);
  });

  it('does not offer a self-edge as somewhere to go', () => {
    // A node pointing at itself is a validation warning the analysis already carries. "Go to where
    // you already are" on top of it would be noise.
    const view = testView();
    const looped = {
      ...view,
      edges: [
        {
          sourceNodeId: 'src/Caller.cs',
          targetNodeId: 'src/Caller.cs',
          kind: 'direct' as const,
          explanation: 'It refers to itself.',
          risks: [],
          crossesContainers: false,
        },
      ],
    };

    const { predecessors, successors } = neighboursOf(looped, 'src/Caller.cs');

    expect(predecessors).toHaveLength(0);
    expect(successors).toHaveLength(0);
  });

  it('says so when a node is isolated rather than inventing a neighbour', () => {
    const { predecessors, successors } = neighboursOf(testView(), 'src/Notes.md');

    expect(predecessors).toHaveLength(0);
    expect(successors).toHaveLength(0);
  });
});

describe('readingPosition', () => {
  it('walks the recommended order end to end', () => {
    const view = fanInFanOutView();

    expect(readingPosition(view, 'src/A.cs')).toEqual({
      position: 1,
      total: 6,
      previousId: null,
      nextId: 'src/B.cs',
    });

    expect(readingPosition(view, 'src/Down2.cs')).toEqual({
      position: 6,
      total: 6,
      previousId: 'src/Down1.cs',
      nextId: null,
    });
  });

  it('reports position zero for a node the reading order never mentions', () => {
    // The order can be short of the graph — the analysis records a diagnostic when it is — and the
    // navigation has to degrade to "no linear path from here" rather than to a wrong one.
    expect(readingPosition(testView(), 'src/Caller.cs').position).toBe(0);
  });
});
