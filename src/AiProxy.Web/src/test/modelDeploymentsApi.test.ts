import { afterEach, describe, expect, it, vi } from 'vitest';
import { updateEndpoint } from '../api/modelDeployments';

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
}

describe('model deployments API', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('updates only deployment assignment settings', async () => {
    const fetchMock = vi.fn(async (url: string, _init?: RequestInit) => {
      if (url === '/api/v1/auth/antiforgery') {
        return jsonResponse({ token: 'csrf-token' });
      }
      return jsonResponse({ id: 'endpoint-id' });
    });
    vi.stubGlobal('fetch', fetchMock);

    await updateEndpoint('gpt-5.4-mini', 'endpoint-id', {
      priority: 1,
      enabled: true,
    });

    const updateCall = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(updateCall?.[0]).toBe('/api/v1/config/model-deployments/gpt-5.4-mini/endpoints/endpoint-id');
    expect(JSON.parse(String(updateCall?.[1]?.body))).toEqual({ priority: 1, enabled: true });
  });
});