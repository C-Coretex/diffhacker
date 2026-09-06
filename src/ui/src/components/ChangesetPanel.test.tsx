import { beforeEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ChangedFileInfo, ChangesetResult, RepositoryInfo } from '@/contracts';
import { RpcProvider } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { FakeTransport } from '@/test/fakeTransport';
import { ChangesetPanel } from './ChangesetPanel';

const repository: RepositoryInfo = {
  path: '/repos/alpha',
  name: 'alpha',
  hasCommits: true,
  isLinkedWorktree: false,
};

const modified: ChangedFileInfo = {
  path: 'src/Core/Analyzer.cs',
  status: 'modified',
  linesAdded: 12,
  linesRemoved: 3,
  hunkCount: 2,
  isBinary: false,
  isSubmodule: false,
  isSymlink: false,
  isUntracked: false,
  isNestedRepository: false,
  language: 'C#',
  project: 'DiffHacker.Core',
};

const binary: ChangedFileInfo = {
  path: 'assets/logo.png',
  status: 'added',
  isBinary: true,
  isSubmodule: false,
  isSymlink: false,
  isUntracked: true,
  isNestedRepository: false,
  language: 'Image',
  project: 'assets',
};

function changeset(overrides: Partial<ChangesetResult> = {}): ChangesetResult {
  const files = overrides.files ?? [modified, binary];

  return {
    repositoryPath: repository.path,
    isClean: false,
    hasCommits: true,
    untrackedIncluded: true,
    hunkCountsAvailable: true,
    files,
    statistics: {
      totalFiles: files.length,
      totalLinesAdded: 12,
      totalLinesRemoved: 3,
      binaryFiles: 1,
      submoduleFiles: 0,
      untrackedFiles: 1,
      addedFiles: 1,
      modifiedFiles: 1,
      deletedFiles: 0,
      renamedFiles: 0,
      copiedFiles: 0,
      languages: ['C#', 'Image'],
      projects: ['DiffHacker.Core', 'assets'],
    },
    ...overrides,
  };
}

function renderPanel(transport: FakeTransport) {
  return render(
    <RpcProvider transport={transport}>
      <ChangesetPanel />
    </RpcProvider>,
  );
}

async function settleLoad(transport: FakeTransport, result: ChangesetResult) {
  await waitFor(() => expect(transport.lastRequest().method).toBe('changeset.load'));
  transport.respond(result);
}

describe('ChangesetPanel', () => {
  beforeEach(() => {
    useAppStore.setState({
      repositoryInfo: repository,
      repository: 'open',
      changeset: 'idle',
      changesetResult: undefined,
      changesetError: undefined,
      includeUntracked: true,
    });
  });

  it('lists changed files with their status, stats and metadata', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);
    await settleLoad(transport, changeset());

    expect(await screen.findByText('src/Core/Analyzer.cs')).toBeInTheDocument();
    expect(screen.getByText('Modified')).toBeInTheDocument();
    expect(screen.getByText('+12')).toBeInTheDocument();
    expect(screen.getByText('−3')).toBeInTheDocument();
    expect(screen.getByText('C#')).toBeInTheDocument();
    expect(screen.getByText('DiffHacker.Core')).toBeInTheDocument();
  });

  it('asks for untracked files by default', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('changeset.load'));

    // Requirement 2's default. An AI-generated change is mostly new files, and starting with
    // them hidden would show the reviewer a fraction of what happened.
    expect(transport.lastRequest().params).toEqual([
      { repositoryPath: '/repos/alpha', includeUntracked: true },
    ]);
  });

  it('reloads without untracked files when the toggle is cleared', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);
    await settleLoad(transport, changeset());

    await userEvent.click(
      screen.getByLabelText('Include new files git does not track yet'),
    );

    await waitFor(() =>
      expect(transport.lastRequest().params).toEqual([
        { repositoryPath: '/repos/alpha', includeUntracked: false },
      ]),
    );
  });

  it('invents no line counts for a binary file', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);
    await settleLoad(transport, changeset());

    // Verification item 4. Zero added and zero removed would be a claim git never made.
    expect(await screen.findByText('not counted')).toBeInTheDocument();
    expect(screen.getByText('Binary')).toBeInTheDocument();
  });

  it('says a clean working tree is clean rather than showing an empty list', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);

    await settleLoad(
      transport,
      changeset({
        isClean: true,
        files: [],
        statistics: {
          totalFiles: 0,
          totalLinesAdded: 0,
          totalLinesRemoved: 0,
          binaryFiles: 0,
          submoduleFiles: 0,
          untrackedFiles: 0,
          addedFiles: 0,
          modifiedFiles: 0,
          deletedFiles: 0,
          renamedFiles: 0,
          copiedFiles: 0,
          languages: [],
          projects: [],
        },
      }),
    );

    // Requirement 9. An empty list reads as a failure; "nothing to review" reads as good news.
    expect(await screen.findByText('Nothing to review')).toBeInTheDocument();
    expect(
      screen.getByText('Your working tree matches HEAD. Make a change and refresh.'),
    ).toBeInTheDocument();
  });

  it('shows a rename with the path it came from', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);

    await settleLoad(
      transport,
      changeset({
        files: [
          {
            ...modified,
            path: 'src/Core/Renamed.cs',
            previousPath: 'src/Core/Analyzer.cs',
            status: 'renamed',
          },
        ],
      }),
    );

    expect(await screen.findByText('Renamed')).toBeInTheDocument();
    expect(screen.getByText('was src/Core/Analyzer.cs')).toBeInTheDocument();
  });

  it('fetches and shows the diff for a file on demand', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);
    await settleLoad(transport, changeset({ files: [modified] }));

    await userEvent.click(await screen.findByRole('button', { name: 'Show diff' }));

    await waitFor(() => expect(transport.lastRequest().method).toBe('changeset.fileDiff'));
    expect(transport.lastRequest().params).toEqual([
      {
        repositoryPath: '/repos/alpha',
        path: 'src/Core/Analyzer.cs',
        untracked: false,
      },
    ]);

    transport.respond({
      kind: 'text',
      path: 'src/Core/Analyzer.cs',
      sizeBytes: 120,
      unifiedDiff: '@@ -1 +1 @@\n-before\n+after\n',
    });

    expect(await screen.findByText(/\+after/)).toBeInTheDocument();
  });

  it('says an oversized diff is oversized instead of showing nothing', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);
    await settleLoad(transport, changeset({ files: [modified] }));

    await userEvent.click(await screen.findByRole('button', { name: 'Show diff' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('changeset.fileDiff'));

    transport.respond({
      kind: 'too_large',
      path: 'src/Core/Analyzer.cs',
      sizeBytes: 42_000_000,
    });

    expect(await screen.findByText(/40\.1 MB and is too large/)).toBeInTheDocument();
  });

  it('resolves a host error code to a message and never shows the developer detail', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('changeset.load'));
    transport.respondWithError('changeset_repository_unreadable', { path: '/repos/alpha' });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(
      '/repos/alpha could not be read as a git working tree. It may have been moved or deleted.',
    );
    expect(screen.queryByText(/developer detail/)).not.toBeInTheDocument();
  });

  it('warns when hunk counts could not be attributed rather than showing wrong ones', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);
    await settleLoad(transport, changeset({ hunkCountsAvailable: false }));

    expect(
      await screen.findByText(
        'Hunk counts could not be attributed to files on this run, so they are not shown.',
      ),
    ).toBeInTheDocument();
  });

  it('reveals a very large changeset in pages rather than all at once', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);

    const many: ChangedFileInfo[] = Array.from({ length: 250 }, (_, index) => ({
      ...modified,
      path: `src/file${index}.cs`,
    }));

    await settleLoad(transport, changeset({ files: many }));

    // Mounting fifteen hundred rows on first paint is the case this product exists for, and it
    // has to stay usable.
    expect(await screen.findByText('src/file0.cs')).toBeInTheDocument();
    expect(screen.queryByText('src/file249.cs')).not.toBeInTheDocument();
    expect(screen.getByText('Showing 200 of 250')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Show 50 more' }));

    expect(await screen.findByText('src/file249.cs')).toBeInTheDocument();
  });
});
