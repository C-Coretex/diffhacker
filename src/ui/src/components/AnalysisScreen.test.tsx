import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import type { AnalysisView } from '@/contracts';
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

  it('sends nothing to the host while the reviewer hovers the diagram', async () => {
    // Verification step 7, in the form this level can check: the iteration's first fixed decision
    // is that no LLM call may happen on hover, ever, and the renderer's only route to one is the
    // bridge. Nothing new crosses it.
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('analysis.get'));
    transport.respond(analysedView());

    const box = await screen.findByTestId('graph-node-src/Contract.cs');
    const sentByNow = transport.sent.length;

    await userEvent.hover(box);
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
