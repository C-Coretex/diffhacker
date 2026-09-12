import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { en } from '@/i18n/en';
import { RpcProvider } from '@/rpc/RpcProvider';
import { FakeTransport } from '@/test/fakeTransport';
import { DataManagementForm } from './DataManagementForm';

function requests(transport: FakeTransport, method: string) {
  return transport.sent
    .map((raw) => JSON.parse(raw) as { method: string })
    .filter((request) => request.method === method);
}

describe('DataManagementForm', () => {
  it('asks for confirmation before sending anything', async () => {
    const transport = new FakeTransport();
    render(
      <RpcProvider transport={transport}>
        <DataManagementForm />
      </RpcProvider>,
    );

    await userEvent.click(screen.getByTestId('delete-all-data-button'));

    expect(screen.getByText(en.dataManagement.confirmTitle)).toBeInTheDocument();
    expect(requests(transport, 'data.deleteAll')).toHaveLength(0);
  });

  it('deletes on confirmation and reports success, with nothing left to click', async () => {
    const transport = new FakeTransport();
    render(
      <RpcProvider transport={transport}>
        <DataManagementForm />
      </RpcProvider>,
    );

    await userEvent.click(screen.getByTestId('delete-all-data-button'));
    await userEvent.click(screen.getByTestId('delete-all-data-confirm'));

    await waitFor(() => expect(requests(transport, 'data.deleteAll')).toHaveLength(1));
    act(() => transport.respond(null));

    expect(await screen.findByText(en.dataManagement.done)).toBeInTheDocument();

    // The host is about to close the window; there is nothing left for this screen to offer.
    expect(screen.queryByTestId('delete-all-data-button')).not.toBeInTheDocument();
    expect(screen.queryByText(en.dataManagement.confirmTitle)).not.toBeInTheDocument();
  });

  it('reports a failed wipe and leaves the confirmation open to retry', async () => {
    const transport = new FakeTransport();
    render(
      <RpcProvider transport={transport}>
        <DataManagementForm />
      </RpcProvider>,
    );

    await userEvent.click(screen.getByTestId('delete-all-data-button'));
    await userEvent.click(screen.getByTestId('delete-all-data-confirm'));

    await waitFor(() => expect(requests(transport, 'data.deleteAll')).toHaveLength(1));
    act(() => transport.respondWithError('some_unmapped_failure'));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.getByTestId('delete-all-data-confirm')).toBeInTheDocument();
  });
});
