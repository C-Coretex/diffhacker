import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { RpcProvider } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { FakeTransport } from '@/test/fakeTransport';
import { fanInFanOutView } from '@/graph/testGraph';
import { useReviewShortcuts } from './useReviewShortcuts';

/** A host for the hook, with a text field beside it so the guard can be exercised. */
function Harness() {
  useReviewShortcuts(fanInFanOutView());
  return <input aria-label="search" />;
}

function renderHarness() {
  return render(
    <RpcProvider transport={new FakeTransport()}>
      <Harness />
    </RpcProvider>,
  );
}

describe('useReviewShortcuts', () => {
  beforeEach(() => {
    useAppStore.setState({
      diffNodeId: undefined,
      graphFocusedNodeId: undefined,
      reviewedNodeIds: new Set<string>(),
      graphCollapsed: new Set<string>(),
    });
  });

  it('walks the reading order forwards and backwards', async () => {
    const user = userEvent.setup();
    useAppStore.setState({ diffNodeId: 'src/Hub.cs' });

    renderHarness();

    await user.keyboard('j');
    expect(useAppStore.getState().diffNodeId).toBe('src/Down1.cs');

    await user.keyboard('k');
    expect(useAppStore.getState().diffNodeId).toBe('src/Hub.cs');
  });

  it('starts at the beginning of the reading order when nothing is open yet', async () => {
    const user = userEvent.setup();
    renderHarness();

    await user.keyboard('j');

    expect(useAppStore.getState().diffNodeId).toBe('src/A.cs');
  });

  it('marks and unmarks the open node', async () => {
    const user = userEvent.setup();
    useAppStore.setState({ diffNodeId: 'src/Hub.cs' });

    renderHarness();

    await user.keyboard('r');
    expect(useAppStore.getState().reviewedNodeIds.has('src/Hub.cs')).toBe(true);

    await user.keyboard('r');
    expect(useAppStore.getState().reviewedNodeIds.has('src/Hub.cs')).toBe(false);
  });

  it('folds the open node’s cluster', async () => {
    const user = userEvent.setup();
    useAppStore.setState({ diffNodeId: 'src/Down2.cs' });

    renderHarness();

    await user.keyboard('c');

    expect(useAppStore.getState().graphCollapsed.has('docs')).toBe(true);
  });

  it('closes the panel on Escape and does nothing when there is none open', async () => {
    const user = userEvent.setup();
    useAppStore.setState({ diffNodeId: 'src/Hub.cs' });

    renderHarness();

    await user.keyboard('{Escape}');
    expect(useAppStore.getState().diffNodeId).toBeUndefined();

    await user.keyboard('{Escape}');
    expect(useAppStore.getState().diffNodeId).toBeUndefined();
  });

  it('opens the diff for whatever the diagram is focused on', async () => {
    const user = userEvent.setup();
    useAppStore.setState({ graphFocusedNodeId: 'src/C.cs' });

    renderHarness();

    await user.keyboard('{Enter}');

    expect(useAppStore.getState().diffNodeId).toBe('src/C.cs');
  });

  it('leaves keys alone while the reviewer is typing', async () => {
    // The reason single unmodified keys are safe at all. Someone searching for "reject" would
    // otherwise mark three files reviewed and fold two clusters on the way.
    const user = userEvent.setup();
    useAppStore.setState({ diffNodeId: 'src/Hub.cs' });

    renderHarness();

    await user.click(screen.getByLabelText('search'));
    await user.keyboard('rejck');

    expect(useAppStore.getState().reviewedNodeIds.size).toBe(0);
    expect(useAppStore.getState().diffNodeId).toBe('src/Hub.cs');
    expect(screen.getByLabelText('search')).toHaveValue('rejck');
  });

  it('leaves keys alone while a modifier is held', async () => {
    const user = userEvent.setup();
    useAppStore.setState({ diffNodeId: 'src/Hub.cs' });

    renderHarness();

    await user.keyboard('{Control>}r{/Control}');

    expect(useAppStore.getState().reviewedNodeIds.size).toBe(0);
  });
});
