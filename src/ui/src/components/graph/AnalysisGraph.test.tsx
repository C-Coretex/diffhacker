import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { useAppStore } from '@/store/appStore';
import { twoContainerView } from '@/graph/testGraph';
import { AnalysisGraph } from './AnalysisGraph';

/**
 * The diagram, rendered.
 *
 * jsdom lays nothing out, so nothing here asserts a position — those come from ELK and
 * `layout.snapshot.test.ts` checks them directly, with no DOM in sight. What this file checks is
 * everything else: that the right boxes exist, that collapsing works, that search reaches a node,
 * and that requirement 9's "the user cannot move nodes" is true of the elements as rendered rather
 * than only of the props passed in.
 *
 * There is no Worker in jsdom, so `AnalysisGraph` falls back to the in-process runner — the same
 * seam the snapshot test uses.
 */
function renderGraph() {
  return render(<AnalysisGraph view={twoContainerView()} />);
}

/** ELK runs asynchronously; nothing is on the canvas until it answers. */
async function boxes() {
  await waitFor(() => expect(screen.getByTestId('graph-node-src/Contract.cs')).toBeInTheDocument());
}

describe('AnalysisGraph', () => {
  beforeEach(() => {
    useAppStore.setState({
      graphCollapsed: new Set<string>(),
      graphSearch: '',
      graphFocusedNodeId: undefined,
      graphLegendOpen: false,
      graphOnlyRenderVisible: false,
    });
  });

  it('draws a box for every node and a region for every cluster', async () => {
    renderGraph();
    await boxes();

    expect(screen.getByTestId('graph-node-src/Caller.cs')).toBeInTheDocument();
    expect(screen.getByTestId('graph-node-src/Notes.md')).toBeInTheDocument();
    expect(screen.getByTestId('graph-container-core')).toBeInTheDocument();
    expect(screen.getByTestId('graph-container-docs')).toBeInTheDocument();
  });

  it('marks the entry node of each cluster', async () => {
    // Requirement 3 and verification step 2. Where it sits is ELK's job and is asserted in the
    // snapshot test; that it is identifiable at all is this one's.
    renderGraph();
    await boxes();

    expect(screen.getByTestId('graph-node-src/Contract.cs')).toHaveAttribute('data-entry', 'true');
    expect(screen.getByTestId('graph-node-src/Caller.cs')).not.toHaveAttribute('data-entry');
  });

  it('shows each box its file name, line counts, status, project and rank', async () => {
    // Requirement 2, all five pieces.
    renderGraph();
    await boxes();

    const box = screen.getByTestId('graph-node-src/Contract.cs');

    expect(within(box).getByText('Contract.cs')).toBeInTheDocument();
    expect(within(box).getByText('+12')).toBeInTheDocument();
    expect(within(box).getByText('−3')).toBeInTheDocument();
    expect(within(box).getByText('modified')).toBeInTheDocument();
    expect(within(box).getByText('DiffHacker.Core')).toBeInTheDocument();
    expect(within(box).getByText('1')).toBeInTheDocument();
  });

  it('says so rather than printing +0 −0 when a line count is not knowable', async () => {
    // Absent, not zero, all the way from git: "we did not count" and "we counted nothing" are
    // different claims, and a box reading +0 −0 makes the wrong one.
    const view = twoContainerView();
    render(
      <AnalysisGraph
        view={{
          ...view,
          changedFiles: view.changedFiles.map((file) =>
            file.path === 'src/Contract.cs'
              ? { ...file, linesAdded: undefined, linesRemoved: undefined, isBinary: true }
              : file,
          ),
        }}
      />,
    );

    await boxes();

    expect(within(screen.getByTestId('graph-node-src/Contract.cs')).getByText('binary')).toBeInTheDocument();
  });

  it('shows three co-occurring states at once, in three different channels', async () => {
    // Verification step 4. §0.6 says states co-occur, so none of them may use the one channel the
    // others need: fill is the project (requirement 6), the border is what happened to the file,
    // the badges are risk and the starting point. A node that is changed, risky and the entry point
    // has to read as all three at the same time.
    const view = twoContainerView();
    render(
      <AnalysisGraph
        view={{
          ...view,
          nodes: view.nodes.map((node) =>
            node.id === 'src/Contract.cs'
              ? { ...node, states: ['changed', 'risky', 'entry_point'] as const }
              : node,
          ),
        }}
      />,
    );

    await boxes();
    const box = screen.getByTestId('graph-node-src/Contract.cs');

    // Risky, in the border colour and in a badge.
    expect(box.className).toContain('border-destructive');
    expect(within(box).getByLabelText('Carries a risk')).toBeInTheDocument();

    // The starting point, in its own badge.
    expect(within(box).getByLabelText('Start reading here')).toBeInTheDocument();
    expect(box).toHaveAttribute('data-entry', 'true');

    // And changed rather than added or deleted, which is the border *style* — a separate channel
    // from the colour risky claimed.
    expect(box.className).toContain('border-solid');
  });

  it('collapses a cluster to one box and puts its nodes back on expanding', async () => {
    renderGraph();
    await boxes();

    await userEvent.click(screen.getByRole('button', { name: 'Collapse Cluster core' }));

    await waitFor(() =>
      expect(screen.queryByTestId('graph-node-src/Contract.cs')).not.toBeInTheDocument(),
    );
    expect(screen.getByTestId('graph-container-core')).toHaveAttribute('data-collapsed', 'true');

    // What a collapsed cluster is worth showing: how many files, and where reading starts.
    expect(screen.getByText('2 files')).toBeInTheDocument();
    expect(screen.getByText('Starts at Contract.cs')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Expand Cluster core' }));
    await waitFor(() => expect(screen.getByTestId('graph-node-src/Contract.cs')).toBeInTheDocument());
  });

  it('collapses and expands every cluster at once', async () => {
    renderGraph();
    await boxes();

    await userEvent.click(screen.getByRole('button', { name: /Collapse all/ }));

    await waitFor(() => {
      expect(screen.getByTestId('graph-container-core')).toHaveAttribute('data-collapsed', 'true');
      expect(screen.getByTestId('graph-container-docs')).toHaveAttribute('data-collapsed', 'true');
    });

    await userEvent.click(screen.getByRole('button', { name: /Expand all/ }));
    await waitFor(() => expect(screen.getByTestId('graph-node-src/Caller.cs')).toBeInTheDocument());
  });

  it('finds a file by name and focuses it', async () => {
    renderGraph();
    await boxes();

    await userEvent.type(screen.getByLabelText('Find a file on the diagram'), 'Caller');

    // "Caller" matches the path and the node title, and the two are listed under separate headings
    // so a title hit is never mistaken for a filename hit. This takes the file.
    const files = await screen.findByRole('heading', { name: 'Files' });
    await userEvent.click(within(files.parentElement!).getByRole('button', { name: /Caller\.cs/ }));

    await waitFor(() => expect(useAppStore.getState().graphFocusedNodeId).toBe('src/Caller.cs'));
  });

  it('expands a collapsed cluster when a search lands inside it', async () => {
    // Centring on a node inside a folded cluster would pan to an empty patch of canvas and look
    // broken, so the cluster is opened first.
    renderGraph();
    await boxes();

    await userEvent.click(screen.getByRole('button', { name: 'Collapse Cluster core' }));
    await waitFor(() =>
      expect(screen.queryByTestId('graph-node-src/Caller.cs')).not.toBeInTheDocument(),
    );

    await userEvent.type(screen.getByLabelText('Find a file on the diagram'), 'Caller');

    const files = await screen.findByRole('heading', { name: 'Files' });
    await userEvent.click(within(files.parentElement!).getByRole('button', { name: /Caller\.cs/ }));

    await waitFor(() => expect(screen.getByTestId('graph-node-src/Caller.cs')).toBeInTheDocument());
    expect(useAppStore.getState().graphCollapsed.has('core')).toBe(false);
  });

  it('says so when nothing matches, rather than showing an empty list', async () => {
    renderGraph();
    await boxes();

    await userEvent.type(screen.getByLabelText('Find a file on the diagram'), 'zzzznope');

    expect(await screen.findByText('Nothing in this change matches “zzzznope”.')).toBeInTheDocument();
  });

  it('never marks a node draggable', async () => {
    // Requirement 9 and verification step 7. React Flow marks a draggable node with a class, so
    // this is checkable on the rendered element rather than only on the props handed in.
    renderGraph();
    await boxes();

    for (const id of ['src/Contract.cs', 'src/Caller.cs', 'src/Notes.md']) {
      const box = screen.getByTestId(`graph-node-${id}`);
      const wrapper = box.closest('.react-flow__node');

      expect(wrapper).not.toBeNull();
      expect(wrapper?.classList.contains('draggable')).toBe(false);
    }
  });

  it('explains the lines and the colours in a legend', async () => {
    // Requirements 4 and 6 both ask for one.
    renderGraph();
    await boxes();

    await userEvent.click(screen.getByRole('button', { name: /Legend/ }));

    const projects = await screen.findByRole('heading', { name: 'Projects' });
    const legend = projects.closest('section')!;

    expect(screen.getByText('Direct — real code connects the two')).toBeInTheDocument();
    expect(screen.getByText('Conceptual — connected by intent, not by code')).toBeInTheDocument();

    // Scoped: the project name is printed on every box as well, which is the point — colour never
    // carries the category on its own.
    expect(within(legend).getByText('DiffHacker.Core')).toBeInTheDocument();
    expect(within(legend).getByText('docs')).toBeInTheDocument();
  });
});
