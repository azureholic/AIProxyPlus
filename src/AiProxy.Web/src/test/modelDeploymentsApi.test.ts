import { afterEach, describe, expect, it, vi } from 'vitest';
import { updateEndpoint } from '../api/modelDeployments';

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
}

describe('model deployments API', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sends editable managed identity endpoint settings', async () => {
    const fetchMock = vi.fn(async (url: string, _init?: RequestInit) => {
      if (url === '/api/v1/auth/antiforgery') {
        return jsonResponse({ token: 'csrf-token' });
      }
      return jsonResponse({ id: 'endpoint-id' });
    });
    vi.stubGlobal('fetch', fetchMock);

    await updateEndpoint('gpt-5.4-mini', 'endpoint-id', {
      baseUrl: 'https://example.openai.azure.com',
      managedIdentityScope: 'https://ai.azure.com/.default',
      managedIdentityTenantId: '72f988bf-86f1-41af-91ab-2d7cd011db47',
      priority: 1,
      enabled: true,
      timeoutSeconds: 100,
      maxRetries: 2,
    });

    const updateCall = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(updateCall?.[0]).toBe('/api/v1/config/model-deployments/gpt-5.4-mini/endpoints/endpoint-id');
    expect(JSON.parse(String(updateCall?.[1]?.body))).toMatchObject({
      baseUrl: 'https://example.openai.azure.com',
      managedIdentityScope: 'https://ai.azure.com/.default',
      managedIdentityTenantId: '72f988bf-86f1-41af-91ab-2d7cd011db47',
    });
  });
});