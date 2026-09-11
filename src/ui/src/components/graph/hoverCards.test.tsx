import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { edge, testView } from '@/graph/testGraph';
import { en } from '@/i18n/en';
import { AnalysisPartsProvider } from '@/components/analysis/AnalysisParts';
import { NodeExplanation } from '@/components/analysis/NodeExplanation';
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

  it('says an explanation was not asked for, and draws no risk column, on a run that skipped both', () => {
    // An empty explanation on a run told to skip them is not "nothing written", and an empty risk
    // column on a run never asked for risks is not "no risks". Both would mislead.
    const skipped = testView({ risksProduced: false, edgeExplanationsProduced: false });

    render(
      <AnalysisPartsProvider view={skipped}>
        <EdgeHoverCard
          view={skipped}
          count={1}
          edges={[{ ...edge('src/Contract.cs', 'src/Caller.cs', 'direct'), explanation: '' }]}
        />
      </AnalysisPartsProvider>,
    );

    expect(screen.getByText(en.analysis.parts.explanationsNotRequested)).toBeInTheDocument();
    expect(screen.queryByText(en.analysis.hover.nothingWritten)).not.toBeInTheDocument();
    expect(screen.queryByTestId('risk-column')).not.toBeInTheDocument();

    // What the edge is still says itself: its ends and its kind are always asked for.
    expect(screen.getByText('direct')).toBeInTheDocument();
  });
});

describe('NodeExplanation', () => {
  it('says the prose was not asked for rather than showing an empty card', () => {
    const view = testView({ nodeExplanationsProduced: false, risksProduced: false });
    const node = { ...view.nodes[0]!, whatChanged: '', whyItChanged: '' };

    render(
      <AnalysisPartsProvider view={view}>
        <NodeExplanation node={node} />
      </AnalysisPartsProvider>,
    );

    expect(screen.getByText(en.analysis.parts.explanationsNotRequested)).toBeInTheDocument();
    expect(screen.queryByText(en.analysis.nodeWhatChanged)).not.toBeInTheDocument();
    expect(screen.queryByTestId('risk-column')).not.toBeInTheDocument();
  });

  it('draws everything, as before, for an analysis that had every part', () => {
    const view = testView({ nodes: [{ ...testView().nodes[0]!, risks: ['Older clients break.'] }] });

    render(
      <AnalysisPartsProvider view={view}>
        <NodeExplanation node={view.nodes[0]!} />
      </AnalysisPartsProvider>,
    );

    expect(screen.queryByText(en.analysis.parts.explanationsNotRequested)).not.toBeInTheDocument();
    expect(within(screen.getByTestId('risk-column')).getByText('Older clients break.')).toBeInTheDocument();
  });
});
