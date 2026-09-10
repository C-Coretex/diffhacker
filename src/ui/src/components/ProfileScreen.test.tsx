import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import type { ProfileState } from '@/contracts';
import { RpcProvider } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { FakeTransport } from '@/test/fakeTransport';
import { ProfileScreen } from './ProfileScreen';

function emptyState(): ProfileState {
  return {
    repositoryPath: 'C:/repo',
    hasProfile: false,
    modules: [],
    entryPoints: [],
    documentationSources: [],
    userNotes: '',
    customInstructions: '',
    customExcludedGlobs: [],
    effectiveExcludedGlobs: ['**/.env', '**/*.pem'],
    characterBudget: 12000,
    driftSubstantial: false,
  };
}

function profiledState(overrides: Partial<ProfileState> = {}): ProfileState {
  return {
    ...emptyState(),
    hasProfile: true,
    purpose: 'A desktop reviewer for large diffs.',
    architecture: 'A host and a renderer.',
    layering: 'Core references nothing above it.',
    patterns: '- Codes, not prose.',
    testLayout: 'xUnit under tests/.',
    modules: [
      { name: 'Core', path: 'src/Core', summary: 'Domain types.', relatedModules: ['Git'] },
    ],
    entryPoints: [{ path: 'src/Program.cs', purpose: 'Composition root.' }],
    documentationSources: ['README.md'],
    characterCount: 900,
    userSectionCharacters: 0,
    generatedAtUtc: '2026-05-01T00:00:00Z',
    generatedFromCommit: 'abc12345def',
    generatedByProvider: 'Test provider',
    generatedByModel: 'test-model',
    ...overrides,
  };
}

function renderScreen(transport: FakeTransport) {
  return render(
    <RpcProvider transport={transport}>
      <ProfileScreen />
    </RpcProvider>,
  );
}

describe('ProfileScreen', () => {
  beforeEach(() => {
    useAppStore.setState({
      repositoryInfo: { path: 'C:/repo', name: 'repo', hasCommits: true, isLinkedWorktree: false },
      profile: 'idle',
      profileState: undefined,
      profileError: undefined,
      profileRun: 'idle',
      profileRunEvents: [],
      profileRunProgress: undefined,
      profileRunLatest: undefined,
    });
  });

  it('loads the profile for the open repository', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.get'));
    expect(transport.lastRequest().params).toEqual([{ repositoryPath: 'C:/repo' }]);

    transport.respond(emptyState());

    expect(await screen.findByText('No profile yet')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Analyse repository' }),
    ).toBeInTheDocument();
  });

  it('shows where a generated profile came from', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.get'));
    transport.respond(profiledState());

    expect(await screen.findByText(/from commit abc12345 by test-model/)).toBeInTheDocument();
    expect(screen.getByDisplayValue('A desktop reviewer for large diffs.')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Core')).toBeInTheDocument();
  });

  it('warns that regenerating replaces the generated sections but not the notes', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.get'));
    transport.respond(profiledState());

    expect(
      await screen.findByText(/Analysing again replaces everything in this section/),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Everything here survives analysing again, exactly as you typed it/),
    ).toBeInTheDocument();
  });

  it('sends the user sections without the generated ones', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.get'));
    transport.respond(profiledState());

    const notes = await screen.findByLabelText('Your notes');
    await userEvent.type(notes, 'The Legacy project is being deleted.');

    const yours = screen.getByText('Yours').closest('[data-slot="card"]') as HTMLElement;
    await userEvent.click(within(yours).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.saveNotes'));

    const [request] = transport.lastRequest<{ params: [Record<string, unknown>] }>().params;
    expect(request.userNotes).toBe('The Legacy project is being deleted.');
    expect(request).not.toHaveProperty('purpose');
  });

  it('nags to regenerate once the repository has drifted', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.get'));
    transport.respond(
      profiledState({
        driftSubstantial: true,
        driftFilesChanged: 240,
        driftTrackedFiles: 900,
        driftCommitReachable: true,
      }),
    );

    expect(await screen.findByText('This repository has moved on')).toBeInTheDocument();
    expect(screen.getByText(/240 of 900 files differ/)).toBeInTheDocument();
  });

  it('says so when the profile’s commit is gone rather than reporting a count', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.get'));
    transport.respond(profiledState({ driftSubstantial: true, driftCommitReachable: false }));

    expect(
      await screen.findByText(/no longer in the repository, so there is no telling how stale it is/),
    ).toBeInTheDocument();
  });

  it('shows the tool log as the run reports it', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.get'));
    transport.respond(emptyState());

    await userEvent.click(await screen.findByRole('button', { name: 'Analyse repository' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.generate'));

    transport.notify('analysis.progress', {
      sequence: 1,
      message: 'Reading the README',
      phase: 'exploring',
      atUtc: '2026-05-01T00:00:00Z',
    });

    transport.notify('analysis.toolCall', {
      sequence: 1,
      kind: 'tool_started',
      turn: 1,
      atUtc: '2026-05-01T00:00:00Z',
      toolName: 'read_file',
      argumentsPreview: '{"path":"README.md"}',
      isError: false,
      inputTokens: 1200,
      outputTokens: 40,
    });

    expect(await screen.findByText('Reading the README')).toBeInTheDocument();
    expect(screen.getByText('Exploring the repository')).toBeInTheDocument();
    expect(screen.getByText('read_file')).toBeInTheDocument();
    expect(screen.getByText('{"path":"README.md"}')).toBeInTheDocument();
    expect(screen.getByText('running…')).toBeInTheDocument();
    expect(screen.getByText('1,200 in / 40 out')).toBeInTheDocument();

    transport.notify('analysis.toolCall', {
      sequence: 2,
      kind: 'tool_finished',
      turn: 1,
      atUtc: '2026-05-01T00:00:01Z',
      toolName: 'read_file',
      resultPreview: '# DiffHacker',
      resultBytes: 4096,
      durationMs: 12,
      isError: false,
      inputTokens: 1300,
      outputTokens: 60,
    });

    // The finished event takes the place of the running row rather than adding a second one.
    expect(await screen.findByText('# DiffHacker')).toBeInTheDocument();
    expect(screen.queryByText('running…')).not.toBeInTheDocument();
    expect(screen.getAllByText('read_file')).toHaveLength(1);
  });

  it('reports a failed run through the catalogue rather than as developer detail', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.get'));
    transport.respond(emptyState());

    await userEvent.click(await screen.findByRole('button', { name: 'Analyse repository' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.generate'));

    transport.respondWithError('profile_no_provider');

    expect(
      await screen.findByText(
        'No LLM provider is set up. Add one in settings, and mark it active if you have several.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText('developer detail')).not.toBeInTheDocument();
  });

  it('previews every documentation file before anything is written', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.get'));
    transport.respond(profiledState());

    await userEvent.click(await screen.findByRole('button', { name: 'Preview documents' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.previewDocumentation'));

    transport.respond({
      repositoryPath: 'C:/repo',
      target: 'docs_directory',
      previewToken: 'token-1',
      files: [
        { relativePath: 'docs/ARCHITECTURE.md', content: '# Architecture\n', exists: false },
        {
          relativePath: 'docs/MODULES.md',
          content: '# Module map\n',
          exists: true,
          unifiedDiff: '--- a/docs/MODULES.md\n+++ b/docs/MODULES.md\n@@ -1 +1 @@\n-old\n+# Module map\n',
        },
        { relativePath: 'docs/CONVENTIONS.md', content: '# Conventions\n', exists: false },
      ],
    });

    const dialog = await screen.findByRole('alertdialog');

    expect(within(dialog).getByText('docs/ARCHITECTURE.md')).toBeInTheDocument();
    expect(within(dialog).getByText('# Architecture')).toBeInTheDocument();
    expect(within(dialog).getByText('docs/MODULES.md')).toBeInTheDocument();
    expect(within(dialog).getByText('docs/CONVENTIONS.md')).toBeInTheDocument();

    // The overwrite is called out and shown as a diff.
    expect(within(dialog).getByText('This file already exists and would be replaced.')).toBeInTheDocument();
    expect(within(dialog).getByText('-old')).toBeInTheDocument();
  });

  it('writes nothing when the preview is cancelled', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.get'));
    transport.respond(profiledState());

    await userEvent.click(await screen.findByRole('button', { name: 'Preview documents' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.previewDocumentation'));

    transport.respond({
      repositoryPath: 'C:/repo',
      target: 'docs_directory',
      previewToken: 'token-1',
      files: [{ relativePath: 'docs/ARCHITECTURE.md', content: '# Architecture\n', exists: false }],
    });

    const dialog = await screen.findByRole('alertdialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel, write nothing' }));

    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(transport.lastRequest().method).toBe('profile.previewDocumentation');
  });

  it('carries the preview token into the write', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.get'));
    transport.respond(profiledState());

    await userEvent.click(await screen.findByRole('button', { name: 'Preview documents' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.previewDocumentation'));

    transport.respond({
      repositoryPath: 'C:/repo',
      target: 'docs_directory',
      previewToken: 'token-1',
      files: [{ relativePath: 'docs/ARCHITECTURE.md', content: '# Architecture\n', exists: false }],
    });

    const dialog = await screen.findByRole('alertdialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Write to repository' }));

    await waitFor(() => expect(transport.lastRequest().method).toBe('profile.exportDocumentation'));

    const [request] = transport.lastRequest<{ params: [Record<string, unknown>] }>().params;
    expect(request.previewToken).toBe('token-1');
    expect(request.target).toBe('docs_directory');
  });
});
