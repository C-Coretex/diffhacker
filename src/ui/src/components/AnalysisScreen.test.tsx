import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AnalysisFreshness, AnalysisLibrary, AnalysisTrace, AnalysisView } from '@/contracts';
import { en } from '@/i18n/en';
import { RpcProvider } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { FakeTransport } from '@/test/fakeTransport';
import { AnalysisScreen } from './AnalysisScreen';

function emptyView(): AnalysisView {
  return {
    repositoryPath: 'C:/repo',
    hasAnalysis: false,
    summary: '',
    overallRisks: [],
    readingOrder: [],
    containers: [],
    nodes: [],
    edges: [],
    diagnostics: [],
    changedFiles: [],
    reviewedNodeIds: [],
    grouping: 'dependency_flow',
    availableGroupings: ['dependency_flow', 'change_clusters'],
    produceChangeClusters: true,
    implementationGroups: [],
    implementationGroupsProduced: false,
    produceImplementationGroups: true,
    isLatest: false,
    toolCallCount: 0,
  };
}

function analysedView(overrides: Partial<AnalysisView> = {}): AnalysisView {
  return {
    ...emptyView(),
    hasAnalysis: true,
    analysisId: 'analysis1',
    schemaVersion: '1.7.0',
    createdAtUtc: '2026-05-01T00:00:00Z',
    headCommit: 'abc12345def',
    providerDisplayName: 'Test provider',
    model: 'test-model',
    inputTokens: 1200,
    outputTokens: 340,
    costUsd: '0.25',
    durationMs: 42000,
    repairRounds: 1,
    summary: 'The contract grew a tenant field and its caller followed.',
    overallRisks: ['Nothing was added to the tests.'],
    readingOrder: ['src/Contract.cs', 'src/Caller.cs'],
    containers: [
      {
        id: 'core',
        title: 'The contract and its caller',
        summary: 'A field arrived and the call site followed.',
        explanation: 'The contract is the decision; the caller is the consequence.',
        risks: ['The migration cannot be rolled back.'],
        displayOrder: 1,
        entryNodeId: 'src/Contract.cs',
        nodeIds: ['src/Contract.cs', 'src/Caller.cs'],
      },
    ],
    nodes: [
      {
        id: 'src/Contract.cs',
        containerId: 'core',
        filePath: 'src/Contract.cs',
        symbol: '',
        startLine: 0,
        endLine: 0,
        title: 'The changed contract',
        whatChanged: 'A tenant field was added.',
        whyItChanged: 'Every caller now has to pass one.',
        howItAffectsOthers: 'The caller had to change.',
        implementationNotes: '',
        risks: ['Older clients will not send it.'],
        importance: 5,
        rank: 1,
        states: ['changed', 'entry_point'],
      },
      {
        id: 'src/Caller.cs',
        containerId: 'core',
        filePath: 'src/Caller.cs',
        symbol: '',
        startLine: 0,
        endLine: 0,
        title: 'The updated caller',
        whatChanged: 'It passes the tenant.',
        whyItChanged: 'The contract requires it.',
        howItAffectsOthers: '',
        implementationNotes: '',
        risks: [],
        importance: 2,
        rank: 2,
        states: ['changed'],
      },
    ],
    edges: [
      {
        sourceNodeId: 'src/Contract.cs',
        targetNodeId: 'src/Caller.cs',
        kind: 'direct',
        explanation: 'The caller constructs the contract.',
        risks: [],
        crossesContainers: false,
      },
    ],
    diagnostics: [
      {
        severity: 'warning',
        code: 'cycle',
        message: 'These nodes form a cycle: a, b.',
        subject: 'src/Caller.cs',
      },
    ],
    statistics: {
      totalFiles: 2,
      totalLinesAdded: 16,
      totalLinesRemoved: 7,
      addedFiles: 0,
      modifiedFiles: 2,
      deletedFiles: 0,
      renamedFiles: 0,
      copiedFiles: 0,
      binaryFiles: 0,
      languages: ['C#'],
      projects: ['DiffHacker'],
      containerCount: 1,
      nodeCount: 2,
      edgeCount: 1,
      directEdgeCount: 1,
      conceptualEdgeCount: 0,
      largestContainerSize: 2,
      smallestContainerSize: 2,
      riskyNodeCount: 1,
      riskCount: 3,
      longestChain: 2,
      highestFanIn: 1,
      highestFanOut: 1,
      highestFanInNodeId: 'src/Caller.cs',
      highestFanOutNodeId: 'src/Contract.cs',
    },
    ...overrides,
  };
}

function renderScreen(transport: FakeTransport) {
  return render(
    <RpcProvider transport={transport}>
      <AnalysisScreen />
    </RpcProvider>,
  );
}

describe('AnalysisScreen', () => {
  beforeEach(() => {
    useAppStore.setState({
      repositoryInfo: { path: 'C:/repo', name: 'repo', hasCommits: true, isLinkedWorktree: false },
      analysis: 'idle',
      analysisView: undefined,
      analysisError: undefined,
      analysisRun: 'idle',
      analysisRunEvents: [],
      analysisRunProgress: undefined,
      analysisRunLatest: undefined,
      analysisLibrary: undefined,
      analysisFreshness: undefined,
      analysisStaleDismissed: undefined,
      analysisTrace: undefined,
      graphOverviewOpen: false,
    });
  });

  it('reads the stored analysis without running anything', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(emptyView());

    expect(await screen.findByText('This change has not been analysed')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Analyse this change' })).toBeInTheDocument();
  });

  it('asks the next run for implementation groups the way the reviewer last left the box', async () => {
    // The host remembers the choice and reports it on every view, so the box comes back as it was
    // left; ticking it again is an override the run request carries.
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond({ ...emptyView(), produceImplementationGroups: false });

    const box = await screen.findByTestId('toggle-implementation-groups');
    await waitFor(() => expect(box).not.toBeChecked());

    await userEvent.click(box);
    await userEvent.click(screen.getByRole('button', { name: 'Analyse this change' }));

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.run'));

    expect(transport.lastRequest().params[0]).toMatchObject({
      repositoryPath: 'C:/repo',
      changeClusters: true,
      implementationGroups: true,
    });
  });

  it('asks the host for the other grouping and replaces the view with what comes back', async () => {
    // Requirement 2, from the renderer's side: switching is a read of the stored answer, so it goes
    // through analysis.setGrouping and never through analysis.run.
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(analysedView());

    await userEvent.click(await screen.findByTestId('grouping-change_clusters'));

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.setGrouping'));

    // By id: the analysis on screen is the one regrouped, even when it is an earlier run reopened
    // from the library rather than the latest.
    expect(
      transport.lastRequest<{ params: [{ grouping: string; repositoryPath: string }] }>().params[0],
    ).toEqual({ grouping: 'change_clusters', repositoryPath: 'C:/repo', analysisId: 'analysis1' });

    transport.respond(
      analysedView({
        grouping: 'change_clusters',
        containers: [
          {
            id: 'contracts',
            title: 'Contracts',
            summary: 'The shape everything agrees on.',
            explanation: 'Its own concern here.',
            risks: [],
            displayOrder: 1,
            entryNodeId: 'src/Contract.cs',
            nodeIds: ['src/Contract.cs'],
          },
          {
            id: 'call-sites',
            title: 'Call sites',
            summary: 'Where it is constructed.',
            explanation: 'Its own concern here too.',
            risks: [],
            displayOrder: 2,
            entryNodeId: 'src/Caller.cs',
            nodeIds: ['src/Caller.cs'],
          },
        ],
      }),
    );

    // The one-line explanation under the toolbar follows the picture, which is the whole of
    // requirement 3.
    expect(await screen.findByText(en.analysis.graph.grouping.changeClustersBody)).toBeInTheDocument();
    expect(useAppStore.getState().analysisView?.grouping).toBe('change_clusters');

    // No run was started on the way.
    expect(transport.sent.some((message) => message.includes('analysis.run'))).toBe(false);
  });

  it('keeps what is keyed to a node across a grouping switch and drops what is keyed to a cluster', async () => {
    // Requirement 6 for the marks, and the reason the collapsed set cannot come with them: the other
    // grouping's clusters are different clusters, and a container id that happens to match is not
    // the same box.
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(analysedView());

    await waitFor(() => expect(useAppStore.getState().analysisView).toBeDefined());

    useAppStore.setState({
      graphCollapsed: new Set(['core']),
      diffContainerId: 'core',
      diffNodeId: 'src/Caller.cs',
    });

    await userEvent.click(await screen.findByTestId('grouping-change_clusters'));
    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.setGrouping'));

    transport.respond(analysedView({ grouping: 'change_clusters', reviewedNodeIds: ['src/Caller.cs'] }));

    await waitFor(() => expect(useAppStore.getState().analysisView?.grouping).toBe('change_clusters'));

    const state = useAppStore.getState();

    expect(state.graphCollapsed.size).toBe(0);
    expect(state.diffContainerId).toBeUndefined();

    // Node-keyed state survives: the file being read, and the marks.
    expect(state.diffNodeId).toBe('src/Caller.cs');
    expect([...state.reviewedNodeIds]).toEqual(['src/Caller.cs']);
  });

  it('shows the live run and nothing of the result while one is in flight', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(emptyView());

    await userEvent.click(await screen.findByRole('button', { name: 'Analyse this change' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.run'));

    transport.notify('analysis.progress', {
      sequence: 1,
      message: 'Reading the contract',
      phase: 'exploring',
      atUtc: '2026-05-01T00:00:00Z',
    });

    transport.notify('analysis.toolCall', {
      sequence: 1,
      kind: 'tool_started',
      turn: 1,
      atUtc: '2026-05-01T00:00:00Z',
      toolName: 'get_file_diff',
      argumentsPreview: '{"path":"src/Contract.cs"}',
      isError: false,
      inputTokens: 1200,
      outputTokens: 40,
    });

    expect(await screen.findByText('Reading the contract')).toBeInTheDocument();
    expect(screen.getByText('Exploring the repository')).toBeInTheDocument();
    expect(screen.getByText('get_file_diff')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stop' })).toBeInTheDocument();

    // §0.2.8: no part of a result is shown until the whole thing exists.
    expect(screen.queryByText('What this change does')).not.toBeInTheDocument();
  });

  it('shows the whole result once the run finishes', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(emptyView());

    await userEvent.click(await screen.findByRole('button', { name: 'Analyse this change' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.run'));
    transport.respond(analysedView());

    // The band: what the change does and what it risks, both without opening anything.
    expect(
      await screen.findByText('The contract grew a tenant field and its caller followed.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Nothing was added to the tests.')).toBeInTheDocument();
    expect(screen.getByText('Analyse again')).toBeInTheDocument();

    // The numbers the application counted rather than ones the model claimed are one toggle away,
    // because the diagram wants the height more than the statistics want to be permanent.
    expect(screen.queryByText('Longest chain')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Overview' }));
    expect(screen.getByText('Longest chain')).toBeInTheDocument();

    // And the diagram beside it — one box per file, which is what Iteration 8 added.
    await waitFor(() =>
      expect(screen.getByTestId('graph-node-src/Contract.cs')).toBeInTheDocument(),
    );
    expect(screen.getByTestId('graph-node-src/Caller.cs')).toBeInTheDocument();
    expect(screen.getByTestId('graph-container-core')).toBeInTheDocument();
  });

  it('keeps the model’s full text a click away rather than on screen by default', async () => {
    // Iteration 7 rendered all of this on the page. It is behind a disclosure now because the
    // diagram says the same thing in a form that fits on one screen — but it is still the only
    // place the model's own words can be read, so it is still there.
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(analysedView());

    await userEvent.click(await screen.findByRole('button', { name: 'Overview' }));
    expect(screen.getByRole('button', { name: 'Details' })).toBeInTheDocument();

    // Titles are on the diagram's boxes either way, and the prose is on the hover cards — the
    // long-form view is where all of it can be read continuously, so it is still folded.
    expect(screen.queryByText('A tenant field was added.')).not.toBeInTheDocument();
    expect(screen.queryByText('The caller constructs the contract.')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Details' }));

    expect(screen.getByText('A tenant field was added.')).toBeInTheDocument();
    expect(screen.getByText('Every caller now has to pass one.')).toBeInTheDocument();
    expect(screen.getByText('The caller constructs the contract.')).toBeInTheDocument();
    expect(screen.getByText('Start here')).toBeInTheDocument();
  });

  it('keeps risks out of the explanations and in a column of their own', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(analysedView());

    // The change-wide risks are on the band with nothing opened: the top of the screen answers
    // "where is the danger" before the reviewer has done anything.
    const overall = await screen.findByText('Nothing was added to the tests.');

    // Every other risk — cluster, file, link — is collected in one register behind the overview,
    // which is requirement 5's "all flagged risks in one place".
    await userEvent.click(screen.getByRole('button', { name: 'Overview' }));

    const register = screen.getByTestId('risk-register');
    expect(within(register).getByText('The migration cannot be rolled back.')).toBeInTheDocument();
    expect(within(register).getByText('Older clients will not send it.')).toBeInTheDocument();
    expect(within(register).getByText('Nothing was added to the tests.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Details' }));

    const explanation = screen.getByText(
      'The contract is the decision; the caller is the consequence.',
    );

    expect(overall).toBeInTheDocument();

    // The risk is its own element rather than text inside the prose beside it.
    expect(explanation.textContent).not.toContain('rolled back');
  });

  it('sends nothing to the host while the reviewer opens and reads a card', async () => {
    // Verification step 7, in the form this level can check: the iteration's first fixed decision
    // is that no LLM call may happen because a card opened, and the renderer's only route to one is
    // the bridge. Nothing new crosses it. Hovering no longer opens anything at all, so this clicks —
    // the only gesture there is — and confirms hovering elsewhere is truly inert alongside it.
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(analysedView());

    const box = await screen.findByTestId('graph-node-src/Contract.cs');
    const sentByNow = transport.sent.length;

    await userEvent.click(box);
    await screen.findByTestId('graph-hover-card');

    await userEvent.hover(screen.getByTestId('graph-container-core'));
    await userEvent.unhover(screen.getByTestId('graph-container-core'));

    expect(transport.sent).toHaveLength(sentByNow);
  });

  it('renders a warning diagnostic as words rather than as its code', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(analysedView());

    await userEvent.click(await screen.findByRole('button', { name: 'Overview' }));

    expect(screen.getByText('These files depend on each other in a loop.')).toBeInTheDocument();
    expect(screen.queryByText('cycle')).not.toBeInTheDocument();
  });

  it('reports a failed run with the detail the host sent', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(emptyView());

    await userEvent.click(await screen.findByRole('button', { name: 'Analyse this change' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.run'));

    transport.respondWithError('analysis_validation_failed');

    expect(
      await screen.findByText(/could not produce a result that covered the whole change/),
    ).toBeInTheDocument();
  });

  it('translates a provider failure through the run-failure catalogue', async () => {
    // These codes had no home before: describeError only searched `error`, and every llm_* code
    // lives in `runFailure` because a live run shows the same words.
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(emptyView());

    await userEvent.click(await screen.findByRole('button', { name: 'Analyse this change' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.run'));

    transport.respondWithError('llm_context_overflow');

    expect(await screen.findByText(/too large for this model/)).toBeInTheDocument();
  });
});

function library(overrides: Partial<AnalysisLibrary> = {}): AnalysisLibrary {
  return {
    repositoryPath: 'C:/repo',
    retentionLimit: 20,
    entries: [
      {
        analysisId: 'analysis1',
        createdAtUtc: '2026-05-01T00:00:00Z',
        providerDisplayName: 'Test provider',
        model: 'test-model',
        costUsd: '0.25',
        inputTokens: 1200,
        outputTokens: 340,
        durationMs: 42000,
        fileCount: 2,
        linesAdded: 16,
        linesRemoved: 7,
        nodeCount: 2,
        containerCount: 1,
        toolCallCount: 14,
        isLatest: true,
      },
      {
        analysisId: 'analysis0',
        createdAtUtc: '2026-04-30T00:00:00Z',
        providerDisplayName: 'Other provider',
        model: 'older-model',
        inputTokens: 900,
        outputTokens: 200,
        durationMs: 30000,
        fileCount: 2,
        linesAdded: 10,
        linesRemoved: 1,
        nodeCount: 2,
        containerCount: 2,
        toolCallCount: 9,
        isLatest: false,
      },
    ],
    ...overrides,
  };
}

function fresh(overrides: Partial<AnalysisFreshness> = {}): AnalysisFreshness {
  return {
    analysisId: 'analysis1',
    isStale: false,
    basis: 'content',
    headMoved: false,
    recordedHeadCommit: 'abc12345def',
    currentHeadCommit: 'abc12345def',
    modifiedCount: 0,
    addedCount: 0,
    removedCount: 0,
    modifiedPaths: [],
    addedPaths: [],
    removedPaths: [],
    checkedAtUtc: '2026-05-02T00:00:00Z',
    ...overrides,
  };
}

/** The screen with the latest analysis on it, and the library and freshness calls it makes answered. */
async function openAnalysed(transport: FakeTransport, freshness = fresh()) {
  renderScreen(transport);

  await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
  act(() => transport.respondTo('analysis.get', analysedView({ isLatest: true, toolCallCount: 3 })));

  await waitFor(() => expect(requests(transport, 'analysis.list')).toHaveLength(1));
  await waitFor(() => expect(requests(transport, 'analysis.checkFreshness')).toHaveLength(1));

  // Answered and then settled: the check's in-flight guard is released in a `finally`, which runs a
  // microtask after the answer lands.
  await act(async () => {
    transport.respondTo('analysis.list', library());
    transport.respondTo('analysis.checkFreshness', freshness);
    await Promise.resolve();
  });
}

async function openOverview() {
  await userEvent.click(await screen.findByRole('button', { name: en.analysis.overview.toggle }));
}

function requests(transport: FakeTransport, method: string) {
  return transport.sent
    .map((raw) => JSON.parse(raw) as { method: string; params: unknown[] })
    .filter((request) => request.method === method);
}

describe('the analysis library', () => {
  beforeEach(() => {
    useAppStore.setState({
      repositoryInfo: { path: 'C:/repo', name: 'repo', hasCommits: true, isLinkedWorktree: false },
      analysis: 'idle',
      analysisView: undefined,
      analysisRun: 'idle',
      analysisLibrary: undefined,
      analysisFreshness: undefined,
      analysisStaleDismissed: undefined,
      analysisTrace: undefined,
      graphOverviewOpen: false,
    });
  });

  it('lists every earlier run with its model, cost, date and size', async () => {
    const transport = new FakeTransport();
    await openAnalysed(transport);

    await userEvent.click(await screen.findByTestId('analysis-history-button'));

    const entries = await screen.findAllByTestId('analysis-history-entry');
    expect(entries.map((entry) => entry.dataset.analysisId)).toEqual(['analysis1', 'analysis0']);

    const older = entries[1]!;
    expect(within(older).getByTestId('analysis-history-model')).toHaveTextContent('Other provider · older-model');
    expect(within(older).getByTestId('analysis-history-size')).toHaveTextContent('2 file(s) +10 −1');
    expect(within(older).getByTestId('analysis-history-cost')).toHaveTextContent(en.analysis.costUnknown);
    expect(within(entries[0]!).getByText(en.analysis.library.latest)).toBeInTheDocument();
    expect(within(entries[0]!).getByText(en.analysis.library.current)).toBeInTheDocument();
    expect(screen.getByText(/The 20 most recent runs are kept/)).toBeInTheDocument();
  });

  it('reopens an earlier run by id, runs nothing, and says it is not the latest', async () => {
    const transport = new FakeTransport();
    await openAnalysed(transport);

    await userEvent.click(await screen.findByTestId('analysis-history-button'));
    const older = (await screen.findAllByTestId('analysis-history-entry'))[1]!;
    await userEvent.click(within(older).getByTestId('analysis-history-open'));

    await waitFor(() => expect(requests(transport, 'analysis.get')).toHaveLength(2));
    expect(requests(transport, 'analysis.get')[1]!.params[0]).toEqual({
      repositoryPath: 'C:/repo',
      analysisId: 'analysis0',
    });

    act(() => {
      transport.respondTo(
        'analysis.get',
        analysedView({ analysisId: 'analysis0', isLatest: false, createdAtUtc: '2026-04-30T00:00:00Z' }),
      );
    });

    expect(await screen.findByTestId('earlier-run-notice')).toBeInTheDocument();
    expect(requests(transport, 'analysis.run')).toHaveLength(0);

    // And the freshness of the run now on screen is asked about, not the previous one's.
    await waitFor(() =>
      expect(requests(transport, 'analysis.checkFreshness').at(-1)!.params[0]).toEqual({
        repositoryPath: 'C:/repo',
        analysisId: 'analysis0',
      }),
    );
  });

  it('deletes a run only after it is confirmed', async () => {
    const transport = new FakeTransport();
    await openAnalysed(transport);

    await userEvent.click(await screen.findByTestId('analysis-history-button'));
    const older = (await screen.findAllByTestId('analysis-history-entry'))[1]!;
    await userEvent.click(within(older).getByTestId('analysis-history-delete'));

    expect(await screen.findByText(en.analysis.library.deleteTitle)).toBeInTheDocument();
    expect(requests(transport, 'analysis.delete')).toHaveLength(0);

    await userEvent.click(screen.getByTestId('analysis-history-confirm-delete'));

    await waitFor(() => expect(requests(transport, 'analysis.delete')).toHaveLength(1));
    expect(requests(transport, 'analysis.delete')[0]!.params[0]).toEqual({
      repositoryPath: 'C:/repo',
      analysisId: 'analysis0',
    });

    act(() => {
      transport.respondTo('analysis.delete', library({ entries: [library().entries[0]!] }));
    });

    expect(await screen.findByTestId('analysis-history-button')).toHaveAccessibleName('History (1)');
  });
});

describe('a stale analysis', () => {
  beforeEach(() => {
    useAppStore.setState({
      repositoryInfo: { path: 'C:/repo', name: 'repo', hasCommits: true, isLinkedWorktree: false },
      analysis: 'idle',
      analysisView: undefined,
      analysisRun: 'idle',
      analysisLibrary: undefined,
      analysisFreshness: undefined,
      analysisStaleDismissed: undefined,
      analysisTrace: undefined,
    });
  });

  it('shows nothing when the working tree is what was analysed', async () => {
    const transport = new FakeTransport();
    await openAnalysed(transport);

    expect(screen.queryByTestId('stale-analysis-banner')).not.toBeInTheDocument();
  });

  it('says what moved and offers to analyse again', async () => {
    const transport = new FakeTransport();
    await openAnalysed(
      transport,
      fresh({
        isStale: true,
        headMoved: true,
        currentHeadCommit: 'fff00000aaa',
        modifiedCount: 1,
        modifiedPaths: ['src/Contract.cs'],
        addedCount: 1,
        addedPaths: ['src/New.cs'],
      }),
    );

    const banner = await screen.findByTestId('stale-analysis-banner');
    expect(within(banner).getByText(en.analysis.stale.heading)).toBeInTheDocument();
    expect(within(banner).getByText('HEAD has moved from abc12345 to fff00000.')).toBeInTheDocument();
    expect(within(banner).getByText('1 file(s) edited since')).toBeInTheDocument();

    await userEvent.click(within(banner).getByTestId('stale-analysis-reanalyse'));

    await waitFor(() => expect(requests(transport, 'analysis.run')).toHaveLength(1));
  });

  it('stays dismissed for the same difference and comes back for a new one', async () => {
    const stale = fresh({ isStale: true, modifiedCount: 1, modifiedPaths: ['src/Contract.cs'] });
    const transport = new FakeTransport();
    await openAnalysed(transport, stale);

    await userEvent.click(await screen.findByTestId('stale-analysis-dismiss'));
    expect(screen.queryByTestId('stale-analysis-banner')).not.toBeInTheDocument();

    act(() => useAppStore.getState().setAnalysisFreshness(stale));
    expect(screen.queryByTestId('stale-analysis-banner')).not.toBeInTheDocument();

    act(() =>
      useAppStore.getState().setAnalysisFreshness({
        ...stale,
        modifiedCount: 2,
        modifiedPaths: ['src/Caller.cs', 'src/Contract.cs'],
      }),
    );
    expect(await screen.findByTestId('stale-analysis-banner')).toBeInTheDocument();
  });

  it('checks again when the window comes back into focus', async () => {
    const transport = new FakeTransport();
    await openAnalysed(transport);

    const now = Date.now();
    vi.spyOn(Date, 'now').mockReturnValue(now + 60_000);

    try {
      act(() => {
        window.dispatchEvent(new Event('focus'));
      });

      await waitFor(() => expect(requests(transport, 'analysis.checkFreshness')).toHaveLength(2));
    } finally {
      vi.restoreAllMocks();
    }
  });

  it('ignores a verdict about an analysis that is no longer on screen', () => {
    useAppStore.setState({ analysisView: analysedView({ analysisId: 'analysis2' }) });

    useAppStore.getState().setAnalysisFreshness(fresh({ analysisId: 'analysis1', isStale: true }));

    expect(useAppStore.getState().analysisFreshness).toBeUndefined();
  });
});

describe('the tool-call inspector', () => {
  beforeEach(() => {
    useAppStore.setState({
      repositoryInfo: { path: 'C:/repo', name: 'repo', hasCommits: true, isLinkedWorktree: false },
      analysis: 'idle',
      analysisView: undefined,
      analysisRun: 'idle',
      analysisLibrary: undefined,
      analysisFreshness: undefined,
      analysisTrace: undefined,
      graphOverviewOpen: false,
    });
  });

  const trace: AnalysisTrace = {
    analysisId: 'analysis1',
    progressMessages: ['Reading the contract', 'Grouping the change'],
    toolCalls: [
      { ordinal: 1, turn: 1, toolName: 'get_project_profile', argumentsPreview: '{}', resultPreview: '# repo', resultBytes: 512, durationMs: 3, isError: false },
      { ordinal: 2, turn: 1, toolName: 'read_file', argumentsPreview: '{"path":"a"}', resultPreview: 'aaa', resultBytes: 2048, durationMs: 12, isError: false },
      { ordinal: 3, turn: 2, toolName: 'read_file', argumentsPreview: '{"path":"b"}', resultPreview: 'No such file.', resultBytes: 40, durationMs: 1, isError: true },
    ],
  };

  it('fetches the trace only when opened, and shows every call in order with its size', async () => {
    const transport = new FakeTransport();
    await openAnalysed(transport);
    await openOverview();

    const inspector = await screen.findByTestId('tool-call-inspector');
    expect(requests(transport, 'analysis.trace')).toHaveLength(0);

    await userEvent.click(within(inspector).getByRole('button', { name: 'Tool calls (3)' }));

    await waitFor(() => expect(requests(transport, 'analysis.trace')).toHaveLength(1));
    expect(requests(transport, 'analysis.trace')[0]!.params[0]).toEqual({
      repositoryPath: 'C:/repo',
      analysisId: 'analysis1',
    });

    act(() => transport.respondTo('analysis.trace', trace));

    const rows = await within(inspector).findAllByTestId('tool-call-row');
    expect(rows.map((row) => row.dataset.ordinal)).toEqual(['1', '2', '3']);
    expect(rows.map((row) => within(row).getByTestId('tool-call-size').textContent)).toEqual([
      '512 B',
      '2.0 KB',
      '40 B',
    ]);
    expect(within(inspector).getByTestId('tool-call-totals')).toHaveTextContent('3 calls · 2.5 KB returned');
    expect(within(inspector).getByTestId('trace-progress')).toHaveTextContent('Grouping the change');
  });

  it('narrows to one tool without losing the order', async () => {
    const transport = new FakeTransport();
    await openAnalysed(transport);
    await openOverview();

    const inspector = await screen.findByTestId('tool-call-inspector');
    await userEvent.click(within(inspector).getByRole('button', { name: 'Tool calls (3)' }));
    await waitFor(() => expect(requests(transport, 'analysis.trace')).toHaveLength(1));
    act(() => transport.respondTo('analysis.trace', trace));

    await userEvent.click(await within(inspector).findByRole('button', { name: /^read_file · 2/ }));

    const rows = within(inspector).getAllByTestId('tool-call-row');
    expect(rows.map((row) => row.dataset.ordinal)).toEqual(['2', '3']);
  });
});
