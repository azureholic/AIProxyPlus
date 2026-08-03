import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { EndpointsPage } from '../pages/config/EndpointsPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
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
      apiFamily: 'OpenAI',
      authMode: 'ApiKey',
      apiKeyPlaintext: 'secret-value',
      timeoutSeconds: 100,
      maxRetries: 2,
    });
  });
});
