import { afterEach, describe, expect, it, vi } from 'vitest';
import { createModel, updateEndpoint, updateModel } from '../api/models';

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
}

describe('models API', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('updates only model assignment settings', async () => {
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
    expect(updateCall?.[0]).toBe('/api/v1/config/models/gpt-5.4-mini/endpoints/endpoint-id');
    expect(JSON.parse(String(updateCall?.[1]?.body))).toEqual({ priority: 1, enabled: true });
  });

  it('sends capabilities when creating and updating models', async () => {
    const fetchMock = vi.fn(async (url: string, _init?: RequestInit) => {
      if (url === '/api/v1/auth/antiforgery') {
        return jsonResponse({ token: 'csrf-token' });
      }
      return jsonResponse({ id: 'model-id' });
    });
    vi.stubGlobal('fetch', fetchMock);

    await createModel('speech-model', 'Priority', ['SpeechToText', 'TextToSpeech']);
    await updateModel('speech-model', 'Priority', ['TextToSpeech']);

    const createCall = fetchMock.mock.calls.find(([url, init]) => url === '/api/v1/config/models' && init?.method === 'POST');
    expect(JSON.parse(String(createCall?.[1]?.body))).toEqual({
      name: 'speech-model',
      strategy: 'Priority',
      capabilities: ['SpeechToText', 'TextToSpeech'],
    });
    const updateCall = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(JSON.parse(String(updateCall?.[1]?.body))).toEqual({
      strategy: 'Priority',
      capabilities: ['TextToSpeech'],
    });
  });
});