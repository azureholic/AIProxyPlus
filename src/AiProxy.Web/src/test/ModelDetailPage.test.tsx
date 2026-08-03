import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ModelDetailPage } from '../pages/config/ModelDetailPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('ModelDetailPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('assigns an existing endpoint without duplicating its configuration', async () => {
    const model = {
      id: 'model-id',
      name: 'gpt-5.4-mini',
      strategy: 'Priority',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      endpoints: [],
    };
    const endpoint = {
      id: 'endpoint-id',
      name: 'shared-openai',
      baseUrl: 'https://api.openai.com',
      apiFamily: 'OpenAI',
      authMode: 'ApiKey',
      secretRef: 'endpoints/endpoint-id',
      managedIdentityScope: null,
      managedIdentityTenantId: null,
      timeoutSeconds: 100,
      maxRetries: 2,
    };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/v1/auth/antiforgery') {
        return jsonResponse({ token: 'csrf-token' });
      }
      if (init?.method === 'POST') {
        return jsonResponse({ ...endpoint, modelId: model.id, priority: 1, enabled: true, health: 'Unknown' }, 201);
      }
      if (url === '/api/v1/config/endpoints') {
        return jsonResponse([endpoint]);
      }
      return jsonResponse(model);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={['/config/models/gpt-5.4-mini']}>
        <Routes>
          <Route path="/config/models/:name" element={<ModelDetailPage />} />
        </Routes>
      </MemoryRouter>,
    );

    await user.click(await screen.findByRole('button', { name: 'Assign endpoint' }));
    const endpointPicker = await screen.findByRole('combobox', { name: 'Endpoint' });
    await user.selectOptions(endpointPicker, 'endpoint-id');
    fireEvent.click(screen.getByRole('button', { name: 'Assign', hidden: true }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/config/models/gpt-5.4-mini/endpoints',
      expect.objectContaining({ method: 'POST' }),
    ));
    const assignCall = fetchMock.mock.calls.find(([url, init]) => url.toString().endsWith('/endpoints') && init?.method === 'POST');
    expect(JSON.parse(String(assignCall?.[1]?.body))).toEqual({ endpointId: 'endpoint-id', priority: 1, enabled: true });
  });
});
