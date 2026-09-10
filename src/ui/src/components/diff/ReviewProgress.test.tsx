import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { useAppStore } from '@/store/appStore';
import { fanInFanOutView } from '@/graph/testGraph';
import { ReviewProgress } from './ReviewProgress';

describe('ReviewProgress', () => {
  beforeEach(() => {
    useAppStore.setState({ reviewedNodeIds: new Set<string>() });
  });

  it('counts what has been read against what there is', () => {
    const view = fanInFanOutView();
    useAppStore.setState({ reviewedNodeIds: new Set(['src/A.cs', 'src/B.cs']) });

    render(<ReviewProgress nodeIds={view.nodes.map((node) => node.id)} />);

    const bar = screen.getByTestId('review-progress');

    expect(bar).toHaveAttribute('data-reviewed', '2');
    expect(bar).toHaveAttribute('data-total', '6');
    expect(bar).toHaveTextContent('2 of 6');
  });

  it('counts one cluster against itself rather than against the whole change', () => {
    // Requirement 7 asks for both, and the per-container one is only useful if it is genuinely
    // per-container: a cluster of one with its file read is done, whatever the other three hundred
    // files are doing.
    const view = fanInFanOutView();
    useAppStore.setState({ reviewedNodeIds: new Set(['src/Down2.cs']) });

    const docs = view.containers.find((container) => container.id === 'docs')!;

    render(<ReviewProgress nodeIds={docs.nodeIds} />);

    expect(screen.getByTestId('review-progress')).toHaveTextContent('1 of 1');
  });

  it('reports the count to assistive technology as a range, not as a percentage', () => {
    useAppStore.setState({ reviewedNodeIds: new Set(['src/A.cs']) });

    render(<ReviewProgress nodeIds={['src/A.cs', 'src/B.cs', 'src/C.cs']} />);

    const bar = screen.getByRole('progressbar');

    expect(bar).toHaveAttribute('aria-valuenow', '1');
    expect(bar).toHaveAttribute('aria-valuemax', '3');
  });

  it('draws nothing for an empty cluster rather than an empty bar', () => {
    render(<ReviewProgress nodeIds={[]} />);

    expect(screen.queryByTestId('review-progress')).not.toBeInTheDocument();
  });
});
