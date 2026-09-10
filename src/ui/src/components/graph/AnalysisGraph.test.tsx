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
  return render(<AnalysisGraph view={twoContainerView()} onChangeGrouping={() => {}} groupingBusy={false} />);
}

/** ELK runs asynchronously; nothing is on the canvas until it answers. */
async function boxes() {
  await waitFor(() => expect(screen.getByTestId('graph-node-src/Contract.cs')).toBeInTheDocument());
}

/**
 * A pointer that will click inside a hover card.
 *
 * Radix keeps a popper invisible and inert until floating-ui has positioned it, and floating-ui
 * never finishes in a document where every element measures zero — so in jsdom the card's own
 * controls inherit `pointer-events: none` and nothing inside it has an accessible name. The card is
 * in the DOM and correct; jsdom simply cannot place it, which is also why everything in it is
 * queried by label rather than by role here. The end-to-end suite hovers, pins and copies in a real
 * window, where none of this applies.
 */
function insideTheCard() {
  return userEvent.setup({ pointerEventsCheck: 0 });
}

describe('AnalysisGraph', () => {
  beforeEach(() => {
    useAppStore.setState({
      graphCollapsed: new Set<string>(),
      graphSearch: '',
      graphFocusedNodeId: undefined,
      graphLegendOpen: false,
      graphOnlyRenderVisible: false,
      diffNodeId: undefined,
      reviewedNodeIds: new Set<string>(),
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
        onChangeGrouping={() => {}}
        groupingBusy={false}
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
        onChangeGrouping={() => {}}
        groupingBusy={false}
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

  // ---------------------------------------------------------------- explanation cards

  it('explains a node on click, with its risks in a column of their own', async () => {
    // Requirement 1 and verification step 5. The four prose fields are on one side, the risks are
    // on the other, and the separation is structural rather than a matter of how the model wrote
    // the sentences.
    const view = twoContainerView();
    render(
      <AnalysisGraph
        onChangeGrouping={() => {}}
        groupingBusy={false}
        view={{
          ...view,
          nodes: view.nodes.map((node) =>
            node.id === 'src/Contract.cs'
              ? {
                  ...node,
                  howItAffectsOthers: 'The caller has to pass a tenant now.',
                  implementationNotes: 'The order of the two writes matters.',
                  risks: ['Older clients will not send the new field.'],
                }
              : node,
          ),
        }}
      />,
    );

    await boxes();
    await userEvent.click(screen.getByTestId('graph-node-src/Contract.cs'));

    const card = await screen.findByTestId('graph-hover-card');

    expect(within(card).getByText('Something changed.')).toBeInTheDocument();
    expect(within(card).getByText('Because of a decision upstream.')).toBeInTheDocument();
    expect(within(card).getByText('The caller has to pass a tenant now.')).toBeInTheDocument();
    expect(within(card).getByText('The order of the two writes matters.')).toBeInTheDocument();

    // The change statistics requirement 1 also asks for.
    expect(within(card).getByText(/\+12 −3 · modified/)).toBeInTheDocument();

    // And the risk, inside the risk column rather than loose in the prose.
    const risks = within(card).getByTestId('risk-column');
    expect(within(risks).getByText('Older clients will not send the new field.')).toBeInTheDocument();
  });

  it('explains a cluster when its title bar is clicked, with its own risks beside it', async () => {
    // Requirement 3 — from the title bar, and from there only. @see ContainerNode
    const view = twoContainerView();
    render(
      <AnalysisGraph
        onChangeGrouping={() => {}}
        groupingBusy={false}
        view={{
          ...view,
          containers: view.containers.map((container) =>
            container.id === 'core'
              ? { ...container, risks: ['The whole cluster lands in one release.'] }
              : container,
          ),
        }}
      />,
    );

    await boxes();
    await userEvent.click(screen.getByTestId('graph-container-header-core'));

    const card = await screen.findByTestId('graph-hover-card');

    expect(within(card).getByText('What core is about.')).toBeInTheDocument();
    expect(within(card).getByText('The longer account of core.')).toBeInTheDocument();
    expect(
      within(within(card).getByTestId('risk-column')).getByText(
        'The whole cluster lands in one release.',
      ),
    ).toBeInTheDocument();
  });

  it('says nothing when the region of a cluster is clicked, only its title bar', async () => {
    // The region a container occupies is working canvas: the reviewer pans across it, drags over it
    // and reads the boxes inside it — it must not also answer to a click the way the title bar does.
    renderGraph();
    await boxes();

    await userEvent.click(screen.getByTestId('graph-container-core'));
    expect(screen.queryByTestId('graph-hover-card')).not.toBeInTheDocument();

    // And the title bar still answers, so the explanation is reachable, just not from the region.
    await userEvent.click(screen.getByTestId('graph-container-header-core'));
    expect(await screen.findByTestId('graph-hover-card')).toBeInTheDocument();
  });

  it('keeps a card open until it is dismissed, and moves it when something else is clicked', async () => {
    const user = insideTheCard();

    renderGraph();
    await boxes();

    await user.click(screen.getByTestId('graph-node-src/Contract.cs'));

    const card = await screen.findByTestId('graph-hover-card');
    expect(within(card).getByText('src/Contract.cs')).toBeInTheDocument();

    // The pointer moving elsewhere does not touch it — there is nothing left that answers to hover.
    await user.hover(screen.getByTestId('graph-node-src/Caller.cs'));
    expect(within(screen.getByTestId('graph-hover-card')).getByText('src/Contract.cs')).toBeInTheDocument();

    // Clicking another box moves the card there rather than being refused.
    await user.click(screen.getByTestId('graph-node-src/Caller.cs'));
    await waitFor(() =>
      expect(
        within(screen.getByTestId('graph-hover-card')).getByText('src/Caller.cs'),
      ).toBeInTheDocument(),
    );

    // Clicking the same box again puts it away — the gesture that opened it closes it.
    await user.click(screen.getByTestId('graph-node-src/Caller.cs'));
    await waitFor(() => expect(screen.queryByTestId('graph-hover-card')).not.toBeInTheDocument());

    // And so does clicking the empty canvas.
    await user.click(screen.getByTestId('graph-node-src/Caller.cs'));
    await waitFor(() => expect(screen.getByTestId('graph-hover-card')).toBeInTheDocument());

    await user.click(document.querySelector('.react-flow__pane')!);
    await waitFor(() => expect(screen.queryByTestId('graph-hover-card')).not.toBeInTheDocument());
  });

  it('expands a cluster without opening its card over the top', async () => {
    // The chevron is inside the container node, so its click reaches the header unless it is
    // stopped — and a card opened by the act of folding a cluster is a card nobody asked for.
    const user = insideTheCard();

    renderGraph();
    await boxes();

    await user.click(screen.getByRole('button', { name: 'Collapse Cluster core' }));

    await waitFor(() =>
      expect(screen.queryByTestId('graph-node-src/Contract.cs')).not.toBeInTheDocument(),
    );
    expect(screen.queryByTestId('graph-hover-card')).not.toBeInTheDocument();
  });

  it('leaves an open card alone when a different cluster is collapsed', async () => {
    // A card anchored to a box is not the box that just folded away — collapsing `docs` while
    // `core`'s card is open must not be read as a reason to close it.
    const user = insideTheCard();

    renderGraph();
    await boxes();

    await user.click(screen.getByTestId('graph-node-src/Contract.cs'));
    expect(await screen.findByTestId('graph-hover-card')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Collapse Cluster docs' }));

    await waitFor(() =>
      expect(screen.queryByTestId('graph-node-src/Notes.md')).not.toBeInTheDocument(),
    );
    expect(screen.getByTestId('graph-hover-card')).toBeInTheDocument();
  });

  it('closes the card from the button on the card itself', async () => {
    const user = insideTheCard();

    renderGraph();
    await boxes();

    await user.click(screen.getByTestId('graph-node-src/Contract.cs'));
    const card = await screen.findByTestId('graph-hover-card');

    await user.click(within(card).getByLabelText('Close'));
    await waitFor(() => expect(screen.queryByTestId('graph-hover-card')).not.toBeInTheDocument());
  });

  it('copies the exact repository-relative path from a node', async () => {
    // Requirement 7 and verification step 9. The path git spells, not the basename and not the id
    // with a disambiguator appended.
    // `userEvent.setup` installs its own clipboard stand-in on `navigator`, so this goes through
    // the real `copyText` and comes back out of a real read.
    const user = insideTheCard();

    renderGraph();
    await boxes();

    await user.click(screen.getByTestId('graph-node-src/Contract.cs'));
    const card = await screen.findByTestId('graph-hover-card');

    await user.click(within(card).getByLabelText('Copy path'));

    expect(await navigator.clipboard.readText()).toBe('src/Contract.cs');
    expect(await within(card).findByText('Path copied')).toBeInTheDocument();
  });

  it('fades a node the model called trivial, without shrinking or hiding it', async () => {
    // Requirement 6, held against §0.2.5. The emphasis differs; the box does not.
    const view = twoContainerView();
    render(
      <AnalysisGraph
        onChangeGrouping={() => {}}
        groupingBusy={false}
        view={{
          ...view,
          nodes: view.nodes.map((node) =>
            node.id === 'src/Contract.cs'
              ? { ...node, importance: 5 }
              : node.id === 'src/Caller.cs'
                ? { ...node, importance: 1 }
                : node,
          ),
        }}
      />,
    );

    await boxes();

    const important = screen.getByTestId('graph-node-src/Contract.cs');
    const trivial = screen.getByTestId('graph-node-src/Caller.cs');

    expect(important).toHaveAttribute('data-emphasis', 'high');
    expect(trivial).toHaveAttribute('data-emphasis', 'low');
    expect(trivial.className).toContain('opacity-70');

    // Still the same box, still on the diagram, and still able to come back to full strength.
    expect(trivial.style.width).toBe(important.style.width);
    expect(trivial.style.height).toBe(important.style.height);
    expect(trivial.className).toContain('hover:opacity-100');
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

  it('opens the diff on a double-click without spending the single one', async () => {
    // Iteration 9 spent the single click on keeping a hover card open, so Iteration 10 takes the
    // double rather than taking that back. Requirement 1's other route is the button on the card.
    const user = insideTheCard();
    renderGraph();
    await boxes();

    await user.click(screen.getByTestId('graph-node-src/Caller.cs'));
    expect(useAppStore.getState().diffNodeId).toBeUndefined();

    await user.dblClick(screen.getByTestId('graph-node-src/Caller.cs'));
    expect(useAppStore.getState().diffNodeId).toBe('src/Caller.cs');
  });

  it('marks the reviewer’s position on the diagram, and moves it', async () => {
    // Requirement 6. A ring of its own rather than a reuse of the search or jump ring, because all
    // three can be true of different boxes at the same moment.
    renderGraph();
    await boxes();

    useAppStore.getState().openDiffFor('src/Notes.md', 'docs');

    await waitFor(() =>
      expect(screen.getByTestId('graph-node-src/Notes.md')).toHaveAttribute('data-current', 'true'),
    );

    expect(screen.getByTestId('graph-node-src/Caller.cs')).not.toHaveAttribute('data-current');

    useAppStore.getState().openDiffFor('src/Caller.cs', 'core');

    await waitFor(() =>
      expect(screen.getByTestId('graph-node-src/Caller.cs')).toHaveAttribute('data-current', 'true'),
    );

    expect(screen.getByTestId('graph-node-src/Notes.md')).not.toHaveAttribute('data-current');
  });

  it('shows on the box which files have been reviewed', async () => {
    // Requirement 7, on the box rather than only in the panel: what makes a three-hundred-node review
    // survivable is seeing at a glance what is left, and the panel only ever shows one node.
    renderGraph();
    await boxes();

    useAppStore.getState().setReviewed(['src/Caller.cs'], true);

    await waitFor(() =>
      expect(screen.getByTestId('graph-node-src/Caller.cs')).toHaveAttribute('data-reviewed', 'true'),
    );

    expect(screen.getByTestId('graph-node-src/Notes.md')).not.toHaveAttribute('data-reviewed');

    useAppStore.getState().setReviewed(['src/Caller.cs'], false);

    await waitFor(() =>
      expect(screen.getByTestId('graph-node-src/Caller.cs')).not.toHaveAttribute('data-reviewed'),
    );
  });

  it('opens the diff from the hover card, which is where the explanation already is', async () => {
    const user = insideTheCard();
    renderGraph();
    await boxes();

    await user.click(screen.getByTestId('graph-node-src/Caller.cs'));

    await user.click(await screen.findByTestId('open-diff'));

    expect(useAppStore.getState().diffNodeId).toBe('src/Caller.cs');
  });

  it('opens the diff from the button on the box, and closes the open card', async () => {
    // The card is where the *explanation* lives; the box is where the actions live. A reviewer who
    // already knows which file they want should not have to summon a card to reach a button — and
    // pressing one means the reading is over, so the card goes with it rather than landing on top of
    // the panel that just opened.
    const user = insideTheCard();
    renderGraph();
    await boxes();

    await user.click(screen.getByTestId('graph-node-src/Caller.cs'));
    expect(await screen.findByTestId('graph-hover-card')).toBeInTheDocument();

    await user.click(screen.getByTestId('node-open-diff-src/Caller.cs'));

    expect(useAppStore.getState().diffNodeId).toBe('src/Caller.cs');
    await waitFor(() => expect(screen.queryByTestId('graph-hover-card')).not.toBeInTheDocument());
  });

  it('marks a file reviewed from the box it is drawn on', async () => {
    const user = insideTheCard();
    renderGraph();
    await boxes();

    await user.click(screen.getByTestId('node-toggle-reviewed-src/Caller.cs'));

    expect(useAppStore.getState().reviewedNodeIds.has('src/Caller.cs')).toBe(true);

    await waitFor(() =>
      expect(screen.getByTestId('graph-node-src/Caller.cs')).toHaveAttribute('data-reviewed', 'true'),
    );

    await user.click(screen.getByTestId('node-toggle-reviewed-src/Caller.cs'));
    expect(useAppStore.getState().reviewedNodeIds.has('src/Caller.cs')).toBe(false);
  });

  it('offers an editor on the box only when the host found one', async () => {
    // Requirement 3's "handle VS Code not being installed", answered by never drawing a button that
    // cannot work. The diagram asks once for the whole canvas; every box reads that one answer.
    const user = insideTheCard();
    renderGraph();
    await boxes();

    expect(screen.queryByTestId('node-open-in-vscode-src/Caller.cs')).not.toBeInTheDocument();

    useAppStore.getState().setEditors({
      vsCodeAvailable: true,
      visualStudioAvailable: false,
      customDiffCommand: '',
      customOpenCommand: '',
    });

    const button = await screen.findByTestId('node-open-in-vscode-src/Caller.cs');
    expect(screen.queryByTestId('node-open-in-visual_studio-src/Caller.cs')).not.toBeInTheDocument();

    // No client in jsdom, so nothing is sent — what is asserted is that pressing it neither throws
    // nor pins a card over the box.
    await user.click(button);
    expect(screen.queryByTestId('graph-hover-card')).not.toBeInTheDocument();
  });

  it('opens every file in a cluster from its title bar', async () => {
    // A cluster is the unit a reviewer actually reads — it is what the model decided belongs
    // together — so it opens as a whole, in the analysis's own reading order, rather than being
    // picked off the canvas one box at a time.
    const user = insideTheCard();
    renderGraph();
    await boxes();

    await user.click(screen.getByTestId('container-open-all-core'));

    const state = useAppStore.getState();
    expect(state.diffContainerId).toBe('core');
    expect(state.diffNodeId).toBe('src/Contract.cs');
    expect(state.graphFocusedNodeId).toBe('src/Contract.cs');
  });

  it('unfolds a collapsed cluster on the way to opening all of it', async () => {
    const user = insideTheCard();
    renderGraph();
    await boxes();

    useAppStore.getState().toggleContainerCollapsed('docs');

    await waitFor(() =>
      expect(screen.getByTestId('graph-container-docs')).toHaveAttribute('data-collapsed', 'true'),
    );

    await user.click(screen.getByTestId('container-open-all-docs'));

    const state = useAppStore.getState();
    expect(state.diffNodeId).toBe('src/Notes.md');
    expect(state.graphCollapsed.has('docs')).toBe(false);
  });

  it('opens a whole cluster on a double-click, the way a file opens on one', async () => {
    const user = insideTheCard();
    renderGraph();
    await boxes();

    await user.dblClick(screen.getByTestId('graph-container-docs'));

    expect(useAppStore.getState().diffContainerId).toBe('docs');
    expect(useAppStore.getState().diffNodeId).toBe('src/Notes.md');
  });
});
