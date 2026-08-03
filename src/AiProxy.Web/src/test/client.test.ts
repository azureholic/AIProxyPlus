import { afterEach, describe, expect, it, vi } from 'vitest';
import { adminFetch, consumerFetch, adminJson, ApiError, submitLogout } from '../api/client';

describe('api client', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('uses same-origin credentials for adminFetch', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await adminFetch('/api/v1/config/model-deployments');

    const [, init] = fetchMock.mock.calls[0];
    expect(init.credentials).toBe('include');
  });

  it('uses same-origin credentials for consumerFetch', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await consumerFetch('/openai/v1/chat/completions');

    const [, init] = fetchMock.mock.calls[0];
    expect(init.credentials).toBe('include');
  });

  it('adds an antiforgery token to unsafe requests', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ token: 'csrf-token' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await consumerFetch('/openai/v1/chat/completions', { method: 'POST', body: '{}' });

    const [, init] = fetchMock.mock.calls[1];
    expect((init.headers as Headers).get('X-CSRF-TOKEN')).toBe('csrf-token');
  });

  it('submits logout as an antiforgery-protected form navigation', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => undefined);

    await submitLogout();

    const form = document.querySelector<HTMLFormElement>('form[action="/api/v1/auth/logout"]');
    expect(form?.method).toBe('post');
    expect(form?.querySelector<HTMLInputElement>('input[name="__RequestVerificationToken"]')?.value)
      .toBe('csrf-token');
    expect(submit).toHaveBeenCalledOnce();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('signals session expiry without retrying a 401 response', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);
    const listener = vi.fn();
    window.addEventListener('aiproxy:session-expired', listener, { once: true });

    await adminFetch('/api/v1/config/model-deployments');

    expect(listener).toHaveBeenCalledOnce();
    expect(fetchMock).toHaveBeenCalledOnce();
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

  it('surfaces an OpenAI-compatible structured error as a thrown ApiError', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: { code: '404', message: 'Resource not found' } }), {
        status: 404,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(adminJson('/openai/v1/chat/completions')).rejects.toMatchObject({
      status: 404,
      message: 'Resource not found',
    } satisfies Partial<ApiError>);
  });
});
