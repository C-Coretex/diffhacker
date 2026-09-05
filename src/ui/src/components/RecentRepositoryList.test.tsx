import { describe, expect, it, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RpcProvider } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { FakeTransport } from '@/test/fakeTransport';
import { RecentRepositoryList } from './RecentRepositoryList';

const present = {
  path: '/repos/alpha',
  name: 'alpha',
  lastOpenedUtc: '2026-09-01T10:00:00Z',
  available: true,
};

const missing = {
  path: '/repos/gone',
  name: 'gone',
  lastOpenedUtc: '2026-08-01T10:00:00Z',
  available: false,
};

function renderList(transport: FakeTransport, onOpen: (path: string) => void = () => {}) {
  return render(
    <RpcProvider transport={transport}>
      <RecentRepositoryList onOpen={onOpen} disabled={false} />
    </RpcProvider>,
  );
}

describe('RecentRepositoryList', () => {
  beforeEach(() => {
    useAppStore.setState({ recents: 'loading', recentRepositories: [], recentsError: undefined });
  });

  it('lists remembered repositories', async () => {
    const transport = new FakeTransport();
    renderList(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('repository.listRecent'));
    transport.respond({ entries: [present] });

    expect(await screen.findByText('alpha')).toBeInTheDocument();
    expect(screen.getByText('/repos/alpha')).toBeInTheDocument();
  });

  it('keeps a repository that no longer exists, marked, rather than dropping it', async () => {
    const transport = new FakeTransport();
    renderList(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('repository.listRecent'));
    transport.respond({ entries: [missing] });

    // Silently vanishing would leave the user wondering where it went.
    expect(await screen.findByText('gone')).toBeInTheDocument();
    expect(screen.getByText('Missing')).toBeInTheDocument();

    // No Open button for something that cannot be opened.
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument();
  });

  it('offers to remove a missing entry, after confirming', async () => {
    const transport = new FakeTransport();
    renderList(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('repository.listRecent'));
    transport.respond({ entries: [missing] });

    await userEvent.click(await screen.findByRole('button', { name: 'Remove gone from the list' }));
    await userEvent.click(screen.getByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(transport.lastRequest().method).toBe('repository.forgetRecent'));
    expect(transport.lastRequest().params).toEqual([{ path: '/repos/gone' }]);
  });

  it('opens an available entry', async () => {
    const opened: string[] = [];
    const transport = new FakeTransport();
    renderList(transport, (path) => opened.push(path));

    await waitFor(() => expect(transport.lastRequest().method).toBe('repository.listRecent'));
    transport.respond({ entries: [present] });

    await userEvent.click(await screen.findByRole('button', { name: 'Open' }));

    expect(opened).toEqual(['/repos/alpha']);
  });

  it('shows a translated error, never the developer message', async () => {
    const transport = new FakeTransport();
    renderList(transport);

    await waitFor(() => expect(transport.lastRequest().method).toBe('repository.listRecent'));
    transport.respondWithError('settings_store_unavailable');

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('Your settings could not be read.'),
    );
    expect(screen.queryByText(/developer detail/)).not.toBeInTheDocument();
  });
});
