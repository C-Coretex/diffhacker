import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { FileContentInfo } from '@/contracts';
import { en } from '@/i18n/en';
import { RpcProvider } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { FakeTransport } from '@/test/fakeTransport';
import { fanInFanOutView, testView } from '@/graph/testGraph';
import { DiffPanel } from './DiffPanel';

/**
 * Monaco does not run under jsdom — it measures a DOM that has no layout — so the editor is replaced
 * by a stub that records what it was asked for. That is not a shortcut: what the panel is responsible
 * for is *which* two texts, which language mode and which line, and those are exactly what the stub
 * captures. Whether Monaco then draws them is Monaco's business, and the end-to-end suite is where a
 * real editor in a real window proves it.
 */
const monacoProps = vi.hoisted(() => ({ current: null as Record<string, unknown> | null }));

vi.mock('./MonacoDiff', () => ({
  MonacoDiff: (props: Record<string, unknown>) => {
    monacoProps.current = props;
    return <div data-testid="monaco-diff" />;
  },
}));

function content(overrides: Partial<FileContentInfo> = {}): FileContentInfo {
  return {
    kind: 'text',
    text: 'line one\nline two\n',
    sizeBytes: 18,
    encoding: 'utf-8',
    usedFallbackEncoding: false,
    ...overrides,
  };
}

/**
 * Answers the two `changeset.fileContent` calls the panel makes, HEAD first.
 *
 * By method rather than by recency: the header asks `editor.describe` at the same time, so which
 * request is "the last one" depends on the order React happened to flush two effects in.
 */
async function respondWithSides(
  transport: FakeTransport,
  head: FileContentInfo,
  working: FileContentInfo,
) {
  await waitFor(() => expect(fileContentRequests(transport)).toHaveLength(2));

  transport.respondTo('changeset.fileContent', head);
  transport.respondTo('changeset.fileContent', working);
}

function fileContentRequests(transport: FakeTransport) {
  return transport.sent
    .map((raw) => JSON.parse(raw) as { method: string; params: [{ path: string; side: string }] })
    .filter((request) => request.method === 'changeset.fileContent');
}

function saveRequests(transport: FakeTransport) {
  return transport.sent
    .map((raw) => JSON.parse(raw) as { method: string; params: [Record<string, unknown>] })
    .filter((request) => request.method === 'changeset.saveFileContent');
}

/** What Monaco calls on every keystroke — MonacoDiff itself is stubbed above. */
function editModifiedText(text: string) {
  act(() => {
    (monacoProps.current?.onModifiedChange as (value: string) => void)(text);
  });
}

function open(nodeId: string) {
  useAppStore.setState({ diffNodeId: nodeId, reviewedNodeIds: new Set<string>() });
}

describe('DiffPanel', () => {
  beforeEach(() => {
    monacoProps.current = null;
    useAppStore.setState({
      diffNodeId: undefined,
      diffContainerId: undefined,
      diffFullScreen: false,
      diffExplanationOpen: true,
      reviewedNodeIds: new Set<string>(),
      reviewedError: undefined,
      editorError: undefined,
      editors: undefined,
      graphCollapsed: new Set<string>(),
    });
  });

  it('says what to do when nothing is open rather than showing an empty editor', () => {
    render(
      <RpcProvider transport={new FakeTransport()}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    expect(screen.getByText(en.analysis.diff.empty)).toBeInTheDocument();
    expect(screen.queryByTestId('monaco-diff')).not.toBeInTheDocument();
  });

  it('reads both sides and hands Monaco the node it was opened on', async () => {
    const transport = new FakeTransport();
    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content({ text: 'before\n' }), content({ text: 'after\n' }));

    await waitFor(() => expect(screen.getByTestId('monaco-diff')).toBeInTheDocument());

    expect(monacoProps.current).toMatchObject({
      path: 'src/Caller.cs',
      sideBySide: true,
    });

    expect(monacoProps.current?.content).toMatchObject({
      kind: 'text',
      original: 'before\n',
      modified: 'after\n',
      degraded: false,
    });
  });

  it('scrolls to the node when the node is a place inside a file, not the whole file', async () => {
    // Requirement 1's second half, and verification step 2. The panel's job is passing the range on;
    // the range coming from the node rather than from line 1 is what makes the diff open where the
    // explanation is about.
    const transport = new FakeTransport();
    const view = testView();
    const scoped = {
      ...view,
      nodes: view.nodes.map((node) =>
        node.id === 'src/Caller.cs' ? { ...node, startLine: 120, endLine: 148 } : node,
      ),
    };

    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={scoped} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('monaco-diff')).toBeInTheDocument());

    expect(monacoProps.current).toMatchObject({ startLine: 120, endLine: 148 });
  });

  it('reads the committed side of a renamed file from the path it had before, and shows both', async () => {
    const transport = new FakeTransport();
    const view = testView();
    const renamed = {
      ...view,
      changedFiles: view.changedFiles.map((file) =>
        file.path === 'src/Caller.cs'
          ? { ...file, status: 'renamed' as const, previousPath: 'src/OldCaller.cs' }
          : file,
      ),
    };

    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={renamed} />
      </RpcProvider>,
    );

    await waitFor(() => expect(fileContentRequests(transport)).toHaveLength(2));

    // The HEAD request asks for the old path — reading the new one would find nothing and draw the
    // rename as an addition.
    const [head, working] = fileContentRequests(transport);
    expect(head?.params[0]).toMatchObject({ path: 'src/OldCaller.cs', side: 'head' });
    expect(working?.params[0]).toMatchObject({ path: 'src/Caller.cs', side: 'working_tree' });

    transport.respondTo('changeset.fileContent', content());
    transport.respondTo('changeset.fileContent', content());

    // Requirement 8: "renamed file (both paths shown)".
    await waitFor(() => expect(screen.getByTestId('renamed-from')).toBeInTheDocument());
    expect(screen.getByTestId('renamed-from')).toHaveTextContent('src/OldCaller.cs');
  });

  it.each([
    ['binary', { kind: 'binary' as const, sizeBytes: 41_000 }, en.analysis.diff.binaryHeading],
    ['too_large', { kind: 'too_large' as const, sizeBytes: 9_000_000 }, en.analysis.diff.tooLargeHeading],
  ])('states plainly that %s has no diff to read, and never mounts an editor', async (
    _name,
    side,
    heading,
  ) => {
    const transport = new FakeTransport();
    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(side), content(side));

    await waitFor(() => expect(screen.getByTestId('diff-unavailable')).toBeInTheDocument());
    expect(screen.getByText(heading)).toBeInTheDocument();
    expect(screen.queryByTestId('monaco-diff')).not.toBeInTheDocument();

    // The size is still reported, and it is the true one — the host sends it even when it withheld
    // the content.
    expect(screen.getByTestId('diff-unavailable')).toHaveTextContent(/KB|MB/);
  });

  it('opens a deleted file with only the committed side', async () => {
    const transport = new FakeTransport();
    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(
      transport,
      content({ text: 'the file as it was\n' }),
      content({ kind: 'absent', text: undefined, sizeBytes: 0 }),
    );

    await waitFor(() => expect(screen.getByTestId('monaco-diff')).toBeInTheDocument());

    expect(monacoProps.current?.content).toMatchObject({
      kind: 'text',
      original: 'the file as it was\n',
      modified: null,
    });
  });

  it('opens an added file with only the working-tree side', async () => {
    const transport = new FakeTransport();
    open('src/Notes.md');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(
      transport,
      content({ kind: 'absent', text: undefined, sizeBytes: 0 }),
      content({ text: '# new\n' }),
    );

    await waitFor(() => expect(screen.getByTestId('monaco-diff')).toBeInTheDocument());

    expect(monacoProps.current?.content).toMatchObject({ original: null, modified: '# new\n' });
  });

  it('turns highlighting off above the size threshold and says why', async () => {
    // Requirement 9: a very large file must not freeze the interface, and the reviewer is told what
    // was traded rather than left to wonder why the colours went.
    const transport = new FakeTransport();
    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content({ sizeBytes: 2_000_000 }), content({ sizeBytes: 2_000_000 }));

    await waitFor(() => expect(screen.getByTestId('degraded-notice')).toBeInTheDocument());
    expect(monacoProps.current?.content).toMatchObject({ degraded: true });
  });

  it('admits when a file was not valid UTF-8', async () => {
    const transport = new FakeTransport();
    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(
      transport,
      content({ encoding: 'iso-8859-1', usedFallbackEncoding: true }),
      content(),
    );

    await waitFor(() => expect(screen.getByTestId('encoding-notice')).toBeInTheDocument());
  });

  it('keeps the explanation and the risks beside the code', async () => {
    // Requirement 4: "the reviewer should not return to the graph to remember why they are here".
    const transport = new FakeTransport();
    const view = testView();
    const risky = {
      ...view,
      nodes: view.nodes.map((node) =>
        node.id === 'src/Caller.cs' ? { ...node, risks: ['The retry loop is now unbounded.'] } : node,
      ),
    };

    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={risky} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());

    await waitFor(() => expect(screen.getByTestId('node-explanation')).toBeInTheDocument());

    expect(screen.getByText('Something changed.')).toBeInTheDocument();
    expect(screen.getByText('Because of a decision upstream.')).toBeInTheDocument();

    // In the risk column, never folded into the prose.
    expect(screen.getByTestId('risk-column')).toHaveTextContent('The retry loop is now unbounded.');
  });

  it('marks a node reviewed and puts the mark back when the host refuses it', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('toggle-reviewed')).toBeInTheDocument());

    await user.click(screen.getByTestId('toggle-reviewed'));

    // Optimistic: the box answers the click, not the network.
    expect(useAppStore.getState().reviewedNodeIds.has('src/Caller.cs')).toBe(true);

    transport.respondWithError('analysis_node_not_found');

    await waitFor(() =>
      expect(useAppStore.getState().reviewedNodeIds.has('src/Caller.cs')).toBe(false),
    );

    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('offers the graph as a labelled choice, not a single next button', async () => {
    // Verification step 5: three predecessors and two successors, each label saying something useful.
    const transport = new FakeTransport();
    open('src/Hub.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={fanInFanOutView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('node-navigator')).toBeInTheDocument());

    expect(screen.getByTestId('predecessors').querySelectorAll('li')).toHaveLength(3);
    expect(screen.getByTestId('successors').querySelectorAll('li')).toHaveLength(2);

    const choice = screen.getByTestId('neighbour-src/Down2.cs');
    expect(choice).toHaveTextContent('src/Down2.cs');
    expect(choice).toHaveTextContent('Read src/Down2.cs after src/Hub.cs.');
    expect(choice).toHaveAttribute('data-kind', 'conceptual');
  });

  it('follows a chosen neighbour, expanding its cluster on the way', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    useAppStore.setState({ graphCollapsed: new Set(['docs']) });
    open('src/Hub.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={fanInFanOutView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('node-navigator')).toBeInTheDocument());

    await user.click(screen.getByTestId('neighbour-src/Down2.cs'));

    const state = useAppStore.getState();

    expect(state.diffNodeId).toBe('src/Down2.cs');

    // The diagram goes with the panel: same node focused, and the cluster it lives in unfolded, so
    // centring lands on a box rather than on an empty patch of canvas.
    expect(state.graphFocusedNodeId).toBe('src/Down2.cs');
    expect(state.graphCollapsed.has('docs')).toBe(false);
  });

  it('walks the recommended reading order as a linear path', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    open('src/Hub.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={fanInFanOutView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('reading-order-position')).toBeInTheDocument());

    expect(screen.getByTestId('reading-order-position')).toHaveTextContent('4 of 6');

    await user.click(screen.getByTestId('reading-order-next'));
    expect(useAppStore.getState().diffNodeId).toBe('src/Down1.cs');
  });

  it('folds the explanation away and keeps it folded on the next file', async () => {
    // The prose is what a reviewer came for on the first file and dead weight on the twentieth, and
    // on a laptop it is competing with the code for the same three hundred pixels. Folding it says
    // something about how this reviewer reads, so it must not come back with the next node.
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    open('src/Hub.cs');

    const { rerender } = render(
      <RpcProvider transport={transport}>
        <DiffPanel view={fanInFanOutView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('node-explanation')).toBeInTheDocument());

    await user.click(screen.getByTestId('toggle-explanation'));

    expect(screen.queryByTestId('node-explanation')).not.toBeInTheDocument();
    expect(screen.getByTestId('toggle-explanation')).toHaveAttribute('aria-expanded', 'false');

    open('src/Down1.cs');
    rerender(
      <RpcProvider transport={transport}>
        <DiffPanel view={fanInFanOutView()} />
      </RpcProvider>,
    );

    expect(screen.queryByTestId('node-explanation')).not.toBeInTheDocument();
  });

  it('shows only the changes by default, and the whole file when asked', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('monaco-diff')).toBeInTheDocument());

    // A whole-file node: Monaco folds the unchanged runs, which is requirement 2's expandable
    // context and what makes a two-thousand-line diff readable.
    expect(monacoProps.current).toMatchObject({ hideUnchanged: true });

    await user.click(screen.getByTestId('toggle-whole-file'));
    expect(monacoProps.current).toMatchObject({ hideUnchanged: false });
  });

  it('starts unfolded for a node that names lines, because folding is what would hide them', async () => {
    // Requirement 1 over requirement 2, and the same conflict `MonacoDiff` documents: the lines a
    // node is about are usually unchanged, so the default fold is exactly what would hide them.
    const transport = new FakeTransport();
    const view = testView();
    const scoped = {
      ...view,
      nodes: view.nodes.map((node) =>
        node.id === 'src/Caller.cs' ? { ...node, startLine: 120, endLine: 148 } : node,
      ),
    };

    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={scoped} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('monaco-diff')).toBeInTheDocument());

    expect(monacoProps.current).toMatchObject({ hideUnchanged: false });
  });

  it('takes the whole window and gives it back', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('toggle-full-screen')).toBeInTheDocument());

    await user.click(screen.getByTestId('toggle-full-screen'));
    expect(useAppStore.getState().diffFullScreen).toBe(true);

    await user.click(screen.getByTestId('toggle-full-screen'));
    expect(useAppStore.getState().diffFullScreen).toBe(false);
  });

  it('lists every file of a cluster that was opened whole, and walks that list', async () => {
    // Opening a container means opening every file in it. The panel still shows one diff — that is
    // what a diff panel is — but the reviewer can see the whole of what they took on, and "next"
    // means the next file in the cluster rather than in the change.
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    useAppStore.setState({ diffNodeId: 'src/A.cs', diffContainerId: 'core' });

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={fanInFanOutView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('container-strip')).toBeInTheDocument());

    const strip = screen.getByTestId('container-strip');
    expect(strip.querySelectorAll('li')).toHaveLength(5);
    expect(screen.getByTestId('queue-src/Hub.cs')).toBeInTheDocument();

    // Five in the cluster, not six in the change.
    expect(screen.getByTestId('reading-order-position')).toHaveTextContent('1 of 5');
    expect(screen.getByTestId('reading-order-position')).toHaveAttribute('data-scope', 'container');

    await user.click(screen.getByTestId('reading-order-next'));
    expect(useAppStore.getState().diffNodeId).toBe('src/B.cs');
    expect(useAppStore.getState().diffContainerId).toBe('core');

    // Jumping straight to one of them keeps the queue.
    await user.click(screen.getByTestId('queue-src/Down1.cs'));
    expect(useAppStore.getState().diffNodeId).toBe('src/Down1.cs');
    expect(useAppStore.getState().diffContainerId).toBe('core');
  });

  it('leaves the cluster queue when the reviewer follows an edge out of it', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    useAppStore.setState({ diffNodeId: 'src/Hub.cs', diffContainerId: 'core' });

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={fanInFanOutView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('container-strip')).toBeInTheDocument());

    // `src/Down2.cs` is in the other cluster. A queue that kept pointing at the one they left would
    // be a queue they no longer chose.
    await user.click(screen.getByTestId('neighbour-src/Down2.cs'));

    expect(useAppStore.getState().diffNodeId).toBe('src/Down2.cs');
    expect(useAppStore.getState().diffContainerId).toBeUndefined();
  });

  it('puts the queue down without closing the file being read', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    useAppStore.setState({ diffNodeId: 'src/Hub.cs', diffContainerId: 'core' });

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={fanInFanOutView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('leave-container')).toBeInTheDocument());

    await user.click(screen.getByTestId('leave-container'));

    expect(useAppStore.getState().diffContainerId).toBeUndefined();
    expect(useAppStore.getState().diffNodeId).toBe('src/Hub.cs');
    expect(screen.queryByTestId('container-strip')).not.toBeInTheDocument();

    // And the linear path is the whole change again.
    expect(screen.getByTestId('reading-order-position')).toHaveTextContent('4 of 6');
  });

  it('marks an edit dirty, saves it, and clears the mark once the host confirms', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content({ text: 'before\n' }), content({ text: 'after\n' }));
    await waitFor(() => expect(screen.getByTestId('monaco-diff')).toBeInTheDocument());

    expect(screen.getByTestId('save-edit')).toBeDisabled();
    expect(screen.queryByTestId('diff-dirty-indicator')).not.toBeInTheDocument();

    editModifiedText('after, edited\n');

    expect(screen.getByTestId('diff-dirty-indicator')).toBeInTheDocument();
    expect(screen.getByTestId('save-edit')).toBeEnabled();

    await user.click(screen.getByTestId('save-edit'));

    await waitFor(() => expect(saveRequests(transport)).toHaveLength(1));

    expect(saveRequests(transport)[0]!.params[0]).toMatchObject({
      repositoryPath: testView().repositoryPath,
      path: 'src/Caller.cs',
      content: 'after, edited\n',
      expectedContent: 'after\n',
      encoding: 'utf-8',
    });

    transport.respondTo('changeset.saveFileContent', { sizeBytes: 14 });

    await waitFor(() => expect(screen.queryByTestId('diff-dirty-indicator')).not.toBeInTheDocument());
    expect(screen.getByTestId('save-edit')).toBeDisabled();
  });

  it('shows the host refusal when a save conflicts, and leaves the edit dirty to retry', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('monaco-diff')).toBeInTheDocument());

    editModifiedText('edited\n');
    await user.click(screen.getByTestId('save-edit'));

    await waitFor(() => expect(saveRequests(transport)).toHaveLength(1));
    transport.respondWithError('changeset_save_conflict', { path: 'src/Caller.cs' });

    expect(await screen.findByTestId('save-error')).toHaveTextContent(
      en.error.changeset_save_conflict.replace('{path}', 'src/Caller.cs'),
    );

    // Refused, not silently accepted: the mark stays so the reviewer knows to do something about it.
    expect(screen.getByTestId('diff-dirty-indicator')).toBeInTheDocument();
  });

  it('asks before discarding an unsaved edit when the panel is closed', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    open('src/Caller.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={testView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('monaco-diff')).toBeInTheDocument());

    editModifiedText('edited\n');
    await user.click(screen.getByTestId('close-diff'));

    // Not closed yet: the confirmation stands in for the navigation until answered.
    expect(await screen.findByText(en.analysis.diff.discardTitle)).toBeInTheDocument();
    expect(useAppStore.getState().diffNodeId).toBe('src/Caller.cs');

    await user.click(screen.getByText(en.analysis.diff.discardCancel));

    expect(screen.queryByText(en.analysis.diff.discardTitle)).not.toBeInTheDocument();
    expect(useAppStore.getState().diffNodeId).toBe('src/Caller.cs');

    await user.click(screen.getByTestId('close-diff'));
    await user.click(screen.getByTestId('diff-confirm-discard'));

    expect(useAppStore.getState().diffNodeId).toBeUndefined();
  });

  it('asks before discarding an unsaved edit when another file is opened, then opens it', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    open('src/Hub.cs');

    render(
      <RpcProvider transport={transport}>
        <DiffPanel view={fanInFanOutView()} />
      </RpcProvider>,
    );

    await respondWithSides(transport, content(), content());
    await waitFor(() => expect(screen.getByTestId('node-navigator')).toBeInTheDocument());

    editModifiedText('edited\n');
    await user.click(screen.getByTestId('neighbour-src/Down2.cs'));

    expect(await screen.findByText(en.analysis.diff.discardTitle)).toBeInTheDocument();
    expect(useAppStore.getState().diffNodeId).toBe('src/Hub.cs');

    await user.click(screen.getByTestId('diff-confirm-discard'));

    expect(useAppStore.getState().diffNodeId).toBe('src/Down2.cs');
  });
});
