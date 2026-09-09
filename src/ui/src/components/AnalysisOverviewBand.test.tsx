import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import type { AnalysisView } from '@/contracts';
import { collectRisks } from '@/components/analysis/RiskRegister';
import { testView } from '@/graph/testGraph';
import { useAppStore } from '@/store/appStore';
import { AnalysisOverviewBand } from './AnalysisOverviewBand';

/** A result with a risk written at every one of the four levels the schema allows. */
function riskyView(overrides: Partial<AnalysisView> = {}): AnalysisView {
  const base = testView();

  return {
    ...base,
    schemaVersion: '1.8.0',
    createdAtUtc: '2026-05-01T09:00:00Z',
    headCommit: 'abc12345def67890',
    providerDisplayName: 'Test provider',
    model: 'test-model',
    inputTokens: 12000,
    outputTokens: 3400,
    costUsd: '0.2512',
    durationMs: 42000,
    repairRounds: 1,
    overallRisks: ['Nothing in the change is covered by a test.'],
    readingOrder: ['src/Contract.cs', 'src/Caller.cs', 'src/Notes.md'],
    containers: base.containers.map((container) => ({
      ...container,
      risks: ['The cluster changes a contract other repositories depend on.'],
    })),
    nodes: base.nodes.map((node) =>
      node.id === 'src/Caller.cs' ? { ...node, risks: ['The caller still assumes one tenant.'] } : node,
    ),
    edges: base.edges.map((edge) => ({ ...edge, risks: ['The two sides can drift apart.'] })),
    statistics: {
      totalFiles: 3,
      totalLinesAdded: 36,
      totalLinesRemoved: 4,
      addedFiles: 1,
      modifiedFiles: 2,
      deletedFiles: 0,
      renamedFiles: 0,
      copiedFiles: 0,
      binaryFiles: 0,
      languages: ['C#', 'Markdown'],
      projects: ['DiffHacker.Core', 'docs'],
      containerCount: 1,
      nodeCount: 3,
      edgeCount: 1,
      directEdgeCount: 1,
      conceptualEdgeCount: 0,
      largestContainerSize: 3,
      smallestContainerSize: 3,
      riskyNodeCount: 1,
      riskCount: 4,
      longestChain: 2,
      highestFanIn: 1,
      highestFanOut: 1,
      highestFanInNodeId: 'src/Caller.cs',
      highestFanOutNodeId: 'src/Contract.cs',
    },
    ...overrides,
  };
}

/**
 * The band across the top of the analysis screen — Iteration 9's requirement 5.
 *
 * What is checked here is the split: two things are permanent, everything else is one toggle away,
 * and the register behind that toggle really does hold every risk rather than the ones that were
 * easy to reach.
 */
describe('AnalysisOverviewBand', () => {
  beforeEach(() => {
    useAppStore.setState({
      graphOverviewOpen: false,
      graphBandOpen: true,
      graphDetailsOpen: false,
      graphCollapsed: new Set<string>(),
      graphFocusedNodeId: undefined,
    });
  });

  it('always shows what the change does and what it risks', () => {
    render(<AnalysisOverviewBand view={riskyView()} />);

    expect(
      screen.getByText('A contract grew a field and everything downstream followed.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Nothing in the change is covered by a test.')).toBeInTheDocument();

    // The count beside the heading covers every level, not only the change-wide ones on show.
    expect(screen.getByText('4 in all')).toBeInTheDocument();
  });

  it('folds the summary and risks away, keeping the count on the strip', async () => {
    // Prose you have already read is the first thing that should give the diagram its height back.
    // What survives folding is the number — that is the part you want without asking.
    const view = riskyView();
    render(<AnalysisOverviewBand view={view} />);

    await userEvent.click(screen.getByRole('button', { name: /What this change does/ }));

    expect(screen.queryByText(view.summary)).not.toBeInTheDocument();
    expect(screen.queryByText('Nothing in the change is covered by a test.')).not.toBeInTheDocument();
    expect(screen.getByText('4 in all')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /What this change does/ }));
    expect(screen.getByText(view.summary)).toBeInTheDocument();
  });

  it('opens the overview whether or not the summary is folded', async () => {
    // The two disclosures are independent: folding the prose must not take the register with it.
    useAppStore.setState({ graphBandOpen: false });
    render(<AnalysisOverviewBand view={riskyView()} />);

    await userEvent.click(screen.getByRole('button', { name: 'Overview' }));
    expect(screen.getByTestId('risk-register')).toBeInTheDocument();
  });

  it('keeps the rest of the overview one toggle away', async () => {
    render(<AnalysisOverviewBand view={riskyView()} />);

    expect(screen.queryByTestId('reading-order')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Overview' }));

    expect(screen.getByTestId('reading-order')).toBeInTheDocument();
    expect(screen.getByTestId('cluster-list')).toBeInTheDocument();
    expect(screen.getByTestId('risk-register')).toBeInTheDocument();
    expect(screen.getByText('By the numbers')).toBeInTheDocument();
    expect(screen.getByText('This run')).toBeInTheDocument();
  });

  it('lists every risk in the document, from all four places one can be written', async () => {
    // Verification step 6, asserted against the document rather than against the rendering: the
    // count comes from walking the view, so a level the register forgot to visit fails here.
    const view = riskyView();
    render(<AnalysisOverviewBand view={view} />);

    await userEvent.click(screen.getByRole('button', { name: 'Overview' }));

    const expected =
      view.overallRisks.length +
      view.containers.reduce((total, container) => total + container.risks.length, 0) +
      view.nodes.reduce((total, node) => total + node.risks.length, 0) +
      view.edges.reduce((total, edge) => total + edge.risks.length, 0);

    const register = within(screen.getByTestId('risk-register'));

    expect(collectRisks(view)).toHaveLength(expected);
    expect(register.getAllByRole('listitem')).toHaveLength(expected);

    expect(register.getByText('Nothing in the change is covered by a test.')).toBeInTheDocument();
    expect(
      register.getByText('The cluster changes a contract other repositories depend on.'),
    ).toBeInTheDocument();
    expect(register.getByText('The caller still assumes one tenant.')).toBeInTheDocument();
    expect(register.getByText('The two sides can drift apart.')).toBeInTheDocument();

    // And each one says where it came from, so "in one place" does not mean "with the context
    // stripped off".
    expect(register.getByText('The change as a whole')).toBeInTheDocument();
    expect(register.getByText('File · src/Caller.cs')).toBeInTheDocument();
  });

  it('lists the clusters with their sizes and the reading order as a path', async () => {
    render(<AnalysisOverviewBand view={riskyView()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Overview' }));

    expect(
      within(screen.getByTestId('cluster-list')).getByText(/3 files · 2 risks/),
    ).toBeInTheDocument();
    expect(within(screen.getByTestId('reading-order')).getAllByRole('listitem')).toHaveLength(3);
  });

  it('takes the reviewer to a node from the reading order, expanding its cluster first', async () => {
    useAppStore.setState({ graphCollapsed: new Set(['core']) });

    render(<AnalysisOverviewBand view={riskyView()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Overview' }));

    await userEvent.click(
      within(screen.getByTestId('reading-order')).getByRole('button', { name: /Title for src\/Caller/ }),
    );

    expect(useAppStore.getState().graphFocusedNodeId).toBe('src/Caller.cs');
    expect(useAppStore.getState().graphCollapsed.has('core')).toBe(false);
  });

  it('reports the run — model, tokens, duration and what it cost', async () => {
    render(<AnalysisOverviewBand view={riskyView()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Overview' }));

    expect(screen.getByText('test-model')).toBeInTheDocument();
    expect(screen.getByText('Test provider')).toBeInTheDocument();
    expect(screen.getByText('12,000')).toBeInTheDocument();
    expect(screen.getByText('$0.2512')).toBeInTheDocument();
    expect(screen.getByText('42s')).toBeInTheDocument();
    expect(screen.getByText('abc12345')).toBeInTheDocument();
  });

  it('says the cost is unknown rather than zero when the model was not priced', async () => {
    // An unrated model cost an unknown amount. Reporting nothing spent would be a claim the
    // application cannot make.
    render(<AnalysisOverviewBand view={riskyView({ costUsd: undefined })} />);
    await userEvent.click(screen.getByRole('button', { name: 'Overview' }));

    expect(screen.getByText('cost unknown')).toBeInTheDocument();
    expect(screen.queryByText('$0.0000')).not.toBeInTheDocument();
  });

  it('keeps the long-form result folded inside the overview', async () => {
    render(<AnalysisOverviewBand view={riskyView()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Overview' }));

    expect(screen.queryByText('Something changed.')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Details' }));
    expect(screen.getAllByText('Something changed.').length).toBeGreaterThan(0);
  });
});
