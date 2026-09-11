import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import type { AnalysisView } from '@/contracts';
import { en } from '@/i18n/en';
import { implementationView } from '@/graph/testGraph';
import { useAppStore } from '@/store/appStore';
import { AnalysisGraph } from './AnalysisGraph';

/**
 * An abstraction and its implementations, drawn as one box on the real surface.
 *
 * The layout half — one leaf, placed where the first member was, no model edge lost — is asserted on
 * plain objects in `graph/implementationGroups.test.ts`. This is the other half: that every row of a
 * merged box is still, to the reviewer, the node it stands for.
 */
function renderGraph(view: AnalysisView = implementationView()) {
  return render(<AnalysisGraph view={view} onChangeGrouping={() => {}} groupingBusy={false} />);
}

const MERGED = 'graph-merged-src/IStore.cs';

async function laidOut() {
  await waitFor(() => expect(screen.getByTestId('graph-node-src/Caller.cs')).toBeInTheDocument());
}

/** @see AnalysisGraph.test.tsx — jsdom cannot place a popover, so its contents are inert there. */
function insideTheCard() {
  return userEvent.setup({ pointerEventsCheck: 0 });
}

describe('ImplementationGroupNode', () => {
  beforeEach(() => {
    window.localStorage.clear();

    useAppStore.setState({
      graphCollapsed: new Set<string>(),
      graphSearch: '',
      graphFocusedNodeId: undefined,
      graphLegendOpen: false,
      graphOnlyRenderVisible: false,
      graphMergeImplementations: true,
      diffNodeId: undefined,
      reviewedNodeIds: new Set<string>(),
    });
  });

  it('draws the abstraction and its implementations as rows of one box', async () => {
    renderGraph();
    await laidOut();

    const box = await screen.findByTestId(MERGED);

    for (const id of ['src/IStore.cs', 'src/MemoryStore.cs', 'src/DiskStore.cs']) {
      expect(within(box).getByTestId(`graph-node-${id}`)).toBeInTheDocument();
    }

    // The abstraction first, and labelled as what it is.
    const rows = within(box).getAllByTestId(/^graph-node-/);
    expect(rows[0]).toHaveAttribute('data-role', 'abstraction');
    expect(rows[0]).toHaveAttribute('data-entry', 'true');
    expect(rows.slice(1).every((row) => row.getAttribute('data-role') === 'implementation')).toBe(true);

    // The caller is not part of it, and is still its own box.
    expect(within(box).queryByTestId('graph-node-src/Caller.cs')).not.toBeInTheDocument();
  });

  it('explains the row that was clicked, not the box', async () => {
    renderGraph();
    await laidOut();

    await userEvent.click(within(await screen.findByTestId(MERGED)).getByText('MemoryStore.cs'));

    const card = await screen.findByTestId('graph-hover-card');
    expect(within(card).getByText('Title for src/MemoryStore.cs')).toBeInTheDocument();
  });

  it('opens the diff of the row that was double-clicked', async () => {
    const user = insideTheCard();
    renderGraph();
    await laidOut();

    await user.dblClick(within(await screen.findByTestId(MERGED)).getByText('DiskStore.cs'));

    expect(useAppStore.getState().diffNodeId).toBe('src/DiskStore.cs');
  });

  it('marks one row reviewed and leaves the others as they were', async () => {
    const user = insideTheCard();
    renderGraph();
    await laidOut();
    await screen.findByTestId(MERGED);

    await user.click(screen.getByTestId('node-toggle-reviewed-src/MemoryStore.cs'));

    await waitFor(() =>
      expect(screen.getByTestId('graph-node-src/MemoryStore.cs')).toHaveAttribute('data-reviewed', 'true'),
    );

    expect(screen.getByTestId('graph-node-src/IStore.cs')).not.toHaveAttribute('data-reviewed');
    expect(screen.getByTestId('graph-node-src/DiskStore.cs')).not.toHaveAttribute('data-reviewed');
  });

  it('shows the reviewer’s position on the row the diff panel is showing', async () => {
    renderGraph();
    await laidOut();
    await screen.findByTestId(MERGED);

    useAppStore.getState().openDiffFor('src/DiskStore.cs', 'store');

    await waitFor(() =>
      expect(screen.getByTestId('graph-node-src/DiskStore.cs')).toHaveAttribute('data-current', 'true'),
    );

    expect(screen.getByTestId('graph-node-src/IStore.cs')).not.toHaveAttribute('data-current');
  });

  it('takes the box apart when merging is turned off, and remembers that', async () => {
    const user = userEvent.setup();
    renderGraph();
    await laidOut();
    await screen.findByTestId(MERGED);

    const toggle = screen.getByTestId('merge-implementations');
    expect(toggle).toHaveAttribute('aria-pressed', 'true');

    await user.click(toggle);

    await waitFor(() => expect(screen.queryByTestId(MERGED)).not.toBeInTheDocument());

    // Every node is still on the diagram, each as a box of its own.
    for (const id of ['src/IStore.cs', 'src/MemoryStore.cs', 'src/DiskStore.cs', 'src/Caller.cs']) {
      expect(screen.getByTestId(`graph-node-${id}`)).not.toHaveAttribute('data-role');
    }

    expect(toggle).toHaveAttribute('aria-pressed', 'false');
    expect(window.localStorage.getItem('diffhacker.graph.mergeImplementations')).toBe('false');

    await user.click(toggle);
    expect(await screen.findByTestId(MERGED)).toBeInTheDocument();
  });

  it('says a re-run is needed when the analysis was never asked for groups', async () => {
    renderGraph(implementationView({ implementationGroups: [], implementationGroupsProduced: false }));
    await laidOut();

    const toggle = screen.getByTestId('merge-implementations');

    expect(toggle).toBeDisabled();
    expect(toggle).toHaveAttribute('aria-pressed', 'false');
    expect(toggle).toHaveAttribute('title', en.analysis.graph.merged.notAsked);
    expect(screen.queryByTestId(MERGED)).not.toBeInTheDocument();
  });

  it('says the change has nothing to merge when the model found none', async () => {
    renderGraph(implementationView({ implementationGroups: [] }));
    await laidOut();

    const toggle = screen.getByTestId('merge-implementations');

    expect(toggle).toBeDisabled();
    expect(toggle).toHaveAttribute('title', en.analysis.graph.merged.none);
  });
});
