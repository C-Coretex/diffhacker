import { describe, expect, it, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ProviderProfile } from '@/contracts';
import { RpcProvider } from '@/rpc/RpcProvider';
import { useAppStore } from '@/store/appStore';
import { FakeTransport } from '@/test/fakeTransport';
import { SettingsScreen } from './SettingsScreen';

const profile: ProviderProfile = {
  id: 'p1',
  providerType: 'openai',
  displayName: 'Work account',
  model: 'gpt-4o',
  hasApiKey: true,
  isActive: true,
  modelSuggestions: ['gpt-4o', 'gpt-4o-mini'],
};

function renderScreen(transport: FakeTransport) {
  return render(
    <RpcProvider transport={transport}>
      <SettingsScreen />
    </RpcProvider>,
  );
}

async function settleListRequest(transport: FakeTransport, profiles: ProviderProfile[] = []) {
  await waitFor(() => expect(transport.lastRequest().method).toBe('providers.list'));
  transport.respond({ profiles, activeProfileId: profiles[0]?.id });
}

describe('SettingsScreen', () => {
  beforeEach(() => {
    useAppStore.setState({
      providers: 'loading',
      providerProfiles: [],
      activeProviderId: undefined,
      providersError: undefined,
      environmentInfo: {
        gitAvailable: true,
        secretBackend: 'windows_dpapi',
        secretBackendIsFallback: false,
      },
    });
  });

  it('shows a loading state, then an empty state', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);

    expect(await screen.findByText('Loading your providers…')).toBeInTheDocument();

    await settleListRequest(transport);
    expect(
      await screen.findByText('No provider configured yet. Add one to run an analysis.'),
    ).toBeInTheDocument();
  });

  it('states which secret backend is protecting keys', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleListRequest(transport);

    expect(
      await screen.findByText(/Windows DPAPI, tied to your user account/),
    ).toBeInTheDocument();
  });

  it('warns when the machine-derived fallback engaged', async () => {
    useAppStore.setState({
      environmentInfo: {
        gitAvailable: true,
        secretBackend: 'machine_derived',
        secretBackendIsFallback: true,
      },
    });

    const transport = new FakeTransport();
    renderScreen(transport);
    await settleListRequest(transport);

    // The fallback is a weaker promise than a keyring, and the interface says so rather than
    // claiming one that is not there.
    expect(await screen.findByText(/No system keyring was available/)).toBeInTheDocument();
  });

  it('sends the API key exactly once and never renders it back', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleListRequest(transport);

    await userEvent.click(screen.getByRole('button', { name: 'Add a provider' }));

    await userEvent.type(screen.getByLabelText('Name'), 'Work account');
    await userEvent.type(screen.getByLabelText('Model'), 'gpt-4o');
    await userEvent.type(screen.getByLabelText('API key'), 'sk-secret-value-123');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(transport.lastRequest().method).toBe('providers.save'));

    const [payload] = transport.lastRequest<{ params: [Record<string, string>] }>().params;
    expect(payload.apiKey).toBe('sk-secret-value-123');

    // The host answers with a profile carrying hasApiKey and no key. Nothing may put it back
    // on screen (CLAUDE.md §0.2.13).
    transport.respond({ profiles: [profile], activeProfileId: 'p1' });

    await waitFor(() => expect(screen.getByText('Work account')).toBeInTheDocument());
    expect(screen.queryByDisplayValue('sk-secret-value-123')).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain('sk-secret-value-123');
  });

  it('omits the key on an edit that leaves it alone', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleListRequest(transport, [profile]);

    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }));
    await userEvent.clear(screen.getByLabelText('Model'));
    await userEvent.type(screen.getByLabelText('Model'), 'gpt-4o-mini');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(transport.lastRequest().method).toBe('providers.save'));

    const [payload] = transport.lastRequest<{ params: [Record<string, string>] }>().params;
    expect(payload).not.toHaveProperty('apiKey');
    expect(payload.id).toBe('p1');
  });

  it('requires a base URL for an OpenAI-compatible endpoint', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleListRequest(transport);

    await userEvent.click(screen.getByRole('button', { name: 'Add a provider' }));
    await userEvent.selectOptions(screen.getByLabelText('Provider'), 'openai_compatible');

    expect(screen.getByLabelText('Base URL')).toBeRequired();
    expect(
      screen.getByText('Required: the endpoint your OpenAI-compatible server listens on.'),
    ).toBeInTheDocument();
  });

  it('reports a successful connection test with the model count', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleListRequest(transport, [profile]);

    await userEvent.click(await screen.findByRole('button', { name: 'Test connection' }));

    await waitFor(() => expect(transport.lastRequest().method).toBe('providers.testConnection'));
    transport.respond({
      succeeded: true,
      modelVerified: true,
      availableModels: ['gpt-4o', 'gpt-4o-mini'],
    });

    expect(
      await screen.findByText('Connected. 2 model(s) available to this key.'),
    ).toBeInTheDocument();
  });

  it('warns when the key works but the model name is not among the reachable models', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleListRequest(transport, [profile]);

    await userEvent.click(await screen.findByRole('button', { name: 'Test connection' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('providers.testConnection'));

    transport.respond({
      succeeded: true,
      modelVerified: false,
      availableModels: ['o3', 'o4-mini'],
    });

    expect(
      await screen.findByText(
        'Connected, but “gpt-4o” is not among the 2 models this key can reach. Check the spelling.',
      ),
    ).toBeInTheDocument();
  });

  it("shows a translated headline plus the provider's own wording on failure", async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleListRequest(transport, [profile]);

    await userEvent.click(await screen.findByRole('button', { name: 'Test connection' }));
    await waitFor(() => expect(transport.lastRequest().method).toBe('providers.testConnection'));

    transport.respond({
      succeeded: false,
      modelVerified: false,
      availableModels: [],
      failureCode: 'provider_invalid_key',
      providerMessage: 'Incorrect API key provided: ***redacted***.',
      httpStatus: 401,
    });

    // Requirement 5 wants the actual error, not a generic one — so both halves must show.
    expect(await screen.findByText('The provider rejected the API key.')).toBeInTheDocument();
    expect(screen.getByText(/Incorrect API key provided/)).toBeInTheDocument();
    expect(screen.getByText('HTTP 401')).toBeInTheDocument();
  });

  it('cannot test a provider that has no stored key', async () => {
    const transport = new FakeTransport();
    renderScreen(transport);
    await settleListRequest(transport, [{ ...profile, hasApiKey: false }]);

    expect(await screen.findByRole('button', { name: 'Test connection' })).toBeDisabled();
    expect(screen.getAllByText('No API key stored').length).toBeGreaterThan(0);
  });
});
