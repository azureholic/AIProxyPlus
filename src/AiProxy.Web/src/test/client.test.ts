import { afterEach, describe, expect, it, vi } from 'vitest';
import { adminFetch, consumerFetch, adminJson, ApiError } from '../api/client';
import { setAdminApiKey, setConsumerApiKey } from '../api/settingsStore';

describe('api client', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    sessionStorage.clear();
  });

  it('attaches X-Admin-Api-Key for adminFetch', async () => {
    setAdminApiKey('admin-secret');
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await adminFetch('/api/v1/config/model-deployments');

    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Headers).get('X-Admin-Api-Key')).toBe('admin-secret');
  });

  it('attaches X-Api-Key for consumerFetch', async () => {
    setConsumerApiKey('consumer-secret');
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await consumerFetch('/openai/v1/chat/completions');

    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Headers).get('X-Api-Key')).toBe('consumer-secret');
  });

  it('surfaces the backend error body as a thrown ApiError', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: 'Model deployment was not found.' }), {
        status: 404,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(adminJson('/api/v1/config/model-deployments/missing')).rejects.toMatchObject({
      status: 404,
      message: 'Model deployment was not found.',
    } satisfies Partial<ApiError>);
  });
});
