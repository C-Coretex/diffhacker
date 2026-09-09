import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { edge, testView } from '@/graph/testGraph';
import { EdgeHoverCard } from './hoverCards';

/**
 * The edge card, tested as a component rather than through the diagram.
 *
 * React Flow draws no edges at all in jsdom — a node is only edge-worthy once the library has
 * *measured* it, and jsdom measures everything as zero — so hovering a line is something only the
 * end-to-end suite can exercise. What can be checked here is the thing requirement 2 actually asks
 * for: that the card says what the relationship is, whether real code backs it, and keeps its risks
 * in a column.
 *
 * The node and container cards are exercised through the real diagram in `AnalysisGraph.test.tsx`,
 * because those React Flow does render here.
 */
describe('EdgeHoverCard', () => {
  const view = testView();

  it('says what the relationship is and whether code backs it', () => {
    render(
      <EdgeHoverCard
        view={view}
        count={1}
        edges={[{ ...edge('src/Contract.cs', 'src/Caller.cs', 'direct'), risks: ['A contract broke.'] }]}
      />,
    );

    expect(screen.getByText('Read Contract.cs, then Caller.cs')).toBeInTheDocument();
    expect(screen.getByText('Read src/Caller.cs after src/Contract.cs.')).toBeInTheDocument();
    expect(screen.getByText('direct')).toBeInTheDocument();

    // Verification step 5, the edge case of it: risks in a column of their own here too.
    expect(within(screen.getByTestId('risk-column')).getByText('A contract broke.')).toBeInTheDocument();
  });

  it('marks a conceptual link as one, and says when it crosses a cluster', () => {
    // §0.2.6: the two kinds must be distinguishable, and on a card that means naming them.
    render(
      <EdgeHoverCard
        view={view}
        count={1}
        edges={[edge('src/Contract.cs', 'src/Notes.md', 'conceptual', true)]}
      />,
    );

    expect(screen.getByText('conceptual')).toBeInTheDocument();
    expect(screen.getByText('crosses clusters')).toBeInTheDocument();
  });

  it('lists every link a bundle folded up rather than picking one to show', () => {
    // A bundle of two edges is neither direct nor conceptual, and choosing one of them to describe
    // would be the card claiming something the model did not.
    render(
      <EdgeHoverCard
        view={view}
        count={2}
        edges={[
          edge('src/Contract.cs', 'src/Notes.md', 'direct', true),
          edge('src/Caller.cs', 'src/Notes.md', 'conceptual', true),
        ]}
      />,
    );

    expect(screen.getByText(/×2/)).toBeInTheDocument();
    expect(screen.getByText('Read Contract.cs, then Notes.md')).toBeInTheDocument();
    expect(screen.getByText('Read Caller.cs, then Notes.md')).toBeInTheDocument();
    expect(screen.getAllByTestId('risk-column')).toHaveLength(2);
  });
});
