import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { EndpointsPage } from '../pages/config/EndpointsPage';
import type { EndpointConfig } from '../api/types';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function endpoint(overrides: Partial<EndpointConfig> = {}): EndpointConfig {
  return {
    id: 'endpoint-id',
    name: 'shared-openai',
    baseUrl: 'https://api.openai.com',
    authMode: 'ApiKey',
    hasPrimarySecret: true,
    hasSecondarySecret: false,
    activeCredentialSlot: 'Primary',
    fallbackCredentialSlot: null,
    fallbackExpiresAt: null,
    managedIdentityScope: null,
    managedIdentityTenantId: null,
    timeoutSeconds: 100,
    maxRetries: 2,
    ...overrides,
  };
}

describe('EndpointsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('creates a reusable endpoint with its target and credential settings', async () => {
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/v1/auth/antiforgery') {
        return jsonResponse({ token: 'csrf-token' });
      }
      if (init?.method === 'POST') {
        return jsonResponse({ id: 'endpoint-id', ...JSON.parse(String(init.body)) }, 201);
      }
      return jsonResponse([]);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<EndpointsPage />);

    await user.click(await screen.findByRole('button', { name: 'New endpoint' }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'shared-openai' } });
    fireEvent.change(screen.getByLabelText('Base URL'), { target: { value: 'https://api.openai.com' } });
    fireEvent.change(screen.getByLabelText('API key'), { target: { value: 'secret-value' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create', hidden: true }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/config/endpoints',
      expect.objectContaining({ method: 'POST' }),
    ));
    const createCall = fetchMock.mock.calls.find(([url, init]) => url === '/api/v1/config/endpoints' && init?.method === 'POST');
    expect(JSON.parse(String(createCall?.[1]?.body))).toMatchObject({
      name: 'shared-openai',
      baseUrl: 'https://api.openai.com',
      authMode: 'ApiKey',
      apiKeyPlaintext: 'secret-value',
      timeoutSeconds: 100,
      maxRetries: 2,
    });
  });

  it('sets a Secondary credential without retaining its plaintext in the UI', async () => {
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/v1/auth/antiforgery') return jsonResponse({ token: 'csrf-token' });
      if (url.endsWith('/secrets/Secondary') && init?.method === 'PUT') {
        return jsonResponse(endpoint({ hasSecondarySecret: true }));
      }
      return jsonResponse([endpoint()]);
    });
    vi.stubGlobal('fetch', fetchMock);
    const user = userEvent.setup();
    render(<EndpointsPage />);

    await user.click(await screen.findByRole('button', { name: 'Set Secondary' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('New API key'), { target: { value: 'sk-secondary' } });
    await user.click(within(dialog).getByRole('button', { name: 'Rotate', hidden: true }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/config/endpoints/endpoint-id/secrets/Secondary',
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ apiKeyPlaintext: 'sk-secondary' }) }),
    ));
    await waitFor(() => expect(screen.queryByDisplayValue('sk-secondary')).not.toBeInTheDocument());
    expect(document.body).not.toHaveTextContent('sk-secondary');
  });

  it('activates Secondary with a fallback window and rejects a negative value', async () => {
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/v1/auth/antiforgery') return jsonResponse({ token: 'csrf-token' });
      if (url.endsWith('/credential-activation') && init?.method === 'PUT') {
        return jsonResponse(endpoint({ hasSecondarySecret: true, activeCredentialSlot: 'Secondary' }));
      }
      return jsonResponse([endpoint({ hasSecondarySecret: true })]);
    });
    vi.stubGlobal('fetch', fetchMock);
    const user = userEvent.setup();
    render(<EndpointsPage />);

    await user.click(await screen.findByRole('button', { name: 'Activate' }));
    const dialog = await screen.findByRole('dialog');
    const fallbackInput = within(dialog).getByLabelText('Fallback window (minutes)');
    fireEvent.change(fallbackInput, { target: { value: '-1' } });
    expect(within(dialog).getByRole('button', { name: 'Activate', hidden: true })).toBeDisabled();

    fireEvent.change(fallbackInput, { target: { value: '15' } });
    await user.click(within(dialog).getByRole('button', { name: 'Activate', hidden: true }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/config/endpoints/endpoint-id/credential-activation',
      expect.objectContaining({ method: 'PUT' }),
    ));
    const activationCall = fetchMock.mock.calls.find(([url]) => url.endsWith('/credential-activation'));
    const body = JSON.parse(String(activationCall?.[1]?.body));
    expect(body.activeSlot).toBe('Secondary');
    expect(new Date(body.fallbackExpiresAt).getTime()).toBeGreaterThan(Date.now());
  });

  it('shows expired fallback state and exposes only valid stop or removal actions', async () => {
    const endpoints = [
      endpoint({
        id: 'expired-endpoint',
        name: 'expired rotation',
        hasSecondarySecret: true,
        activeCredentialSlot: 'Secondary',
        fallbackCredentialSlot: 'Primary',
        fallbackExpiresAt: '2020-01-01T00:00:00Z',
      }),
      endpoint({ id: 'removable-endpoint', name: 'ready rotation', hasSecondarySecret: true }),
    ];
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/v1/auth/antiforgery') return jsonResponse({ token: 'csrf-token' });
      if (init?.method === 'DELETE') return jsonResponse(endpoint());
      return jsonResponse(endpoints);
    });
    vi.stubGlobal('fetch', fetchMock);
    const user = userEvent.setup();
    render(<EndpointsPage />);

    expect(await screen.findByText(/Fallback Primary expired/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Stop fallback' }));
    await user.click(screen.getByRole('button', { name: 'Remove Secondary' }));

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith(
        '/api/v1/config/endpoints/expired-endpoint/credential-fallback',
        expect.objectContaining({ method: 'DELETE' }),
      );
      expect(fetchMock).toHaveBeenCalledWith(
        '/api/v1/config/endpoints/removable-endpoint/secrets/Secondary',
        expect.objectContaining({ method: 'DELETE' }),
      );
    });
  });
});
