import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import type { AnalysisOptions } from '@/contracts';
import { en } from '@/i18n/en';
import { RpcProvider } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { FakeTransport } from '@/test/fakeTransport';
import { AnalysisDefaultsForm } from './AnalysisDefaultsForm';

const stored: AnalysisOptions = {
  changeClusters: true,
  implementationGroups: false,
  risks: true,
  nodeExplanations: true,
  edgeExplanations: true,
  containerExplanations: true,
  verbosity: 'brief',
};

function requests(transport: FakeTransport, method: string) {
  return transport.sent
    .map((raw) => JSON.parse(raw) as { method: string; params: unknown[] })
    .filter((request) => request.method === method);
}

describe('AnalysisDefaultsForm', () => {
  beforeEach(() => {
    useAppStore.setState({ analysisDefaults: undefined });
  });

  it('shows the stored defaults and saves the whole set when asked', async () => {
    const transport = new FakeTransport();
    render(
      <RpcProvider transport={transport}>
        <AnalysisDefaultsForm />
      </RpcProvider>,
    );

    await waitFor(() => expect(requests(transport, 'analysis.getDefaults')).toHaveLength(1));
    act(() => transport.respondTo('analysis.getDefaults', stored));

    const groups = await screen.findByTestId('default-option-implementation-groups');
    await waitFor(() => expect(groups).not.toBeChecked());
    expect(screen.getByTestId('default-option-verbosity-brief')).toHaveAttribute('aria-pressed', 'true');

    // Every part carries what turning it off leaves, as text rather than a tooltip.
    expect(screen.getByText(en.analysis.parts.risksBody)).toBeInTheDocument();

    await userEvent.click(screen.getByTestId('default-option-risks'));
    await userEvent.click(screen.getByTestId('default-option-verbosity-medium'));

    // Nothing is written until Save: a checkbox is not a commitment.
    expect(requests(transport, 'analysis.saveDefaults')).toHaveLength(0);

    await userEvent.click(screen.getByTestId('analysis-defaults-save'));

    await waitFor(() => expect(requests(transport, 'analysis.saveDefaults')).toHaveLength(1));
    expect(requests(transport, 'analysis.saveDefaults')[0]!.params[0]).toEqual({
      ...stored,
      risks: false,
      verbosity: 'medium',
    });

    act(() => transport.respondTo('analysis.saveDefaults', { ...stored, risks: false, verbosity: 'medium' }));

    expect(await screen.findByText(en.analysis.parts.saved)).toBeInTheDocument();

    // The run options read the same store slice, so they start from what was just saved.
    expect(useAppStore.getState().analysisDefaults).toEqual({ ...stored, risks: false, verbosity: 'medium' });
  });
});
