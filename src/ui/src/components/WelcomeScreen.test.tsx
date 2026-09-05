import { describe, expect, it, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RpcProvider } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { FakeTransport } from '@/test/fakeTransport';
import { WelcomeScreen } from './WelcomeScreen';

function renderScreen(transport: FakeTransport) {
  return render(
    <RpcProvider transport={transport}>
      <WelcomeScreen />
    </RpcProvider>,
  );
}

/** The screen loads its recent list on mount, so most tests have to answer that first. */
async function settleRecentsRequest(transport: FakeTransport, entries: unknown[] = []) {
  await waitFor(() => expect(transport.sent.length).toBeGreaterThan(0));
  transport.respond({ entries });
}

describe('WelcomeScreen', () => {
  beforeEach(() => {
    useAppStore.setState({
      repository: 'none',
      repositoryError: undefined,
      repositoryInfo: undefined,
      recents: 'loading',
      recentRepositories: [],
      recentsError: undefined,
      environment: 'ready',
      environmentInfo: {
        gitAvailable: true,
        gitVersion: 'git version 2.99.0',
        secretBackend: 'windows_dpapi',
        secretBackendIsFallback: false,
      },
      screen: 'welcome',
    });
  });

  it('shows a loading state while the recent list is in flight', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    expect(await screen.findByText('Loading your recent repositories…')).toBeInTheDocument();
  });

  it('shows an empty state when nothing has been opened yet', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleRecentsRequest(transport);

    expect(
      await screen.findByText(
        'Nothing here yet. The repositories you open will be listed for one-click access.',
      ),
    ).toBeInTheDocument();
  });

  it('opens the repository the native picker returned', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleRecentsRequest(transport);

    await userEvent.click(screen.getByRole('button', { name: 'Choose a folder…' }));

    await waitFor(() => expect(transport.lastRequest().method).toBe('repository.browse'));
    transport.respond({ cancelled: false, path: '/repos/alpha' });

    await waitFor(() => expect(transport.lastRequest().method).toBe('repository.open'));
    expect(transport.lastRequest().params).toEqual([{ path: '/repos/alpha' }]);
  });

  it('does nothing when the picker is dismissed', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleRecentsRequest(transport);

    await userEvent.click(screen.getByRole('button', { name: 'Choose a folder…' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('repository.browse'));

    const before = transport.sent.length;
    transport.respond({ cancelled: true });

    // Cancelling is an ordinary outcome: no follow-up call, and no error shown.
    await waitFor(() => expect(transport.sent.length).toBe(before));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('opens a repository from a typed path', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleRecentsRequest(transport);

    await userEvent.type(screen.getByLabelText('Or type a path'), '/repos/beta');
    await userEvent.click(screen.getByRole('button', { name: 'Open' }));

    await waitFor(() => expect(transport.lastRequest().method).toBe('repository.open'));
    expect(transport.lastRequest().params).toEqual([{ path: '/repos/beta' }]);
  });

  it('renders the host error code as a sentence, never the developer message', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleRecentsRequest(transport);

    await userEvent.type(screen.getByLabelText('Or type a path'), '/not/a/repo');
    await userEvent.click(screen.getByRole('button', { name: 'Open' }));

    await waitFor(() => expect(transport.lastRequest().method).toBe('repository.open'));
    transport.respondWithError('repository_is_bare', { path: '/not/a/repo' });

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(
        '/not/a/repo is a bare repository. It has no working tree',
      ),
    );

    expect(screen.queryByText(/developer detail/)).not.toBeInTheDocument();
  });

  it('holds the controls closed until the environment probe has answered', async () => {
    useAppStore.setState({ environment: 'checking', environmentInfo: undefined });

    const transport = new FakeTransport();
    renderScreen(transport);
    await settleRecentsRequest(transport);

    // Otherwise a missing git is discoverable by clicking during the gap.
    expect(await screen.findByText('Checking your environment…')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Choose a folder…' })).toBeDisabled();
  });

  it('disables the controls when git is missing', async () => {
    useAppStore.setState({
      environmentInfo: {
        gitAvailable: false,
        secretBackend: 'machine_derived',
        secretBackendIsFallback: true,
      },
    });

    const transport = new FakeTransport();
    renderScreen(transport);
    await settleRecentsRequest(transport);

    expect(screen.getByRole('button', { name: 'Choose a folder…' })).toBeDisabled();
    expect(screen.getByLabelText('Or type a path')).toBeDisabled();
  });
});
