import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { DemoPanel } from './DemoPanel';
import { RpcProvider } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { FakeTransport } from '@/test/fakeTransport';

function renderPanel(transport: FakeTransport) {
  return render(
    <RpcProvider transport={transport}>
      <DemoPanel />
    </RpcProvider>,
  );
}

describe('DemoPanel', () => {
  beforeEach(() => {
    useAppStore.setState({ demo: 'idle', demoError: undefined, progress: [] });
  });

  it('starts idle and enables the control once the bridge is available', async () => {
    const transport = new FakeTransport();
    renderPanel(transport);

    expect(screen.getByText('Not started.')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Run the round trip' })).toBeEnabled());
  });

  it('calls the host and renders the streamed notifications', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup();
    renderPanel(transport);

    await waitFor(() => expect(screen.getByRole('button', { name: 'Run the round trip' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Run the round trip' }));

    const request = transport.lastRequest();
    expect(request.method).toBe('demo.startCountdown');
    expect(request.params).toEqual([{ steps: 5, delayMilliseconds: 200 }]);

    transport.respond({ operationId: 'op-1', totalSteps: 3 });

    for (let step = 0; step < 3; step++) {
      transport.notify('demo/progress', {
        operationId: 'op-1',
        step,
        totalSteps: 3,
        message: 'demo.step',
        completed: step === 2,
      });
    }

    await waitFor(() => expect(screen.getByText('Stream complete: 3 notification(s) received.')).toBeInTheDocument());
    expect(screen.getByText('Processing step 1 of 3')).toBeInTheDocument();
    expect(screen.getByText('Processing step 3 of 3')).toBeInTheDocument();
  });

  it('shows the resolved message for a host error, not the developer detail', async () => {
    const transport = new FakeTransport();
    const user = userEvent.setup();
    renderPanel(transport);

    await waitFor(() => expect(screen.getByRole('button', { name: 'Run the round trip' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Run the round trip' }));

    transport.respondWithError('demo_steps_out_of_range', { steps: '0' });

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('The host rejected a step count of 0.'));
    expect(screen.queryByText(/developer detail/)).not.toBeInTheDocument();
  });
});
