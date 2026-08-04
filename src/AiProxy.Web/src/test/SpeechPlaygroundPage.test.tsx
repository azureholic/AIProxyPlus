import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SpeechPlaygroundPage } from '../pages/playground/SpeechPlaygroundPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

const models = [
  { id: 'stt', name: 'whisper', strategy: 'Priority', capabilities: ['SpeechToText'], createdAt: '', updatedAt: '', endpoints: [] },
  { id: 'tts', name: 'tts-1', strategy: 'Priority', capabilities: ['TextToSpeech'], createdAt: '', updatedAt: '', endpoints: [] },
  { id: 'chat', name: 'gpt', strategy: 'Priority', capabilities: ['Chat'], createdAt: '', updatedAt: '', endpoints: [] },
];

describe('SpeechPlaygroundPage', () => {
  beforeEach(() => {
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:speech') });
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('uploads audio as multipart data and renders the transcription', async () => {
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/v1/auth/antiforgery') return jsonResponse({ token: 'csrf' });
      if (url === '/api/v1/config/models') return jsonResponse(models);
      if (url === '/openai/v1/audio/transcriptions' && init?.method === 'POST') {
        return jsonResponse({ text: 'Testing one two.' });
      }
      return jsonResponse({});
    });
    vi.stubGlobal('fetch', fetchMock);
    const user = userEvent.setup();
    render(<SpeechPlaygroundPage />);

    await user.click(screen.getByPlaceholderText('Select a model…'));
    await user.click(await screen.findByRole('option', { name: 'whisper' }));
    await user.upload(screen.getByLabelText('Audio file'), new File(['audio'], 'sample.wav', { type: 'audio/wav' }));
    await user.click(screen.getByRole('button', { name: 'Transcribe' }));

    expect(await screen.findByDisplayValue('Testing one two.')).toBeInTheDocument();
    const request = fetchMock.mock.calls.find(([url]) => url === '/openai/v1/audio/transcriptions');
    const body = request?.[1]?.body as FormData;
    expect(body.get('model')).toBe('whisper');
    expect((body.get('file') as File).name).toBe('sample.wav');
  });

  it('requests synthesized audio and offers playback and download', async () => {
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/v1/auth/antiforgery') return jsonResponse({ token: 'csrf' });
      if (url === '/api/v1/config/models') return jsonResponse(models);
      if (url === '/openai/v1/audio/speech' && init?.method === 'POST') {
        return new Response(new Blob(['speech'], { type: 'audio/mpeg' }), {
          status: 200,
          headers: { 'Content-Type': 'audio/mpeg' },
        });
      }
      return jsonResponse({});
    });
    vi.stubGlobal('fetch', fetchMock);
    const user = userEvent.setup();
    render(<SpeechPlaygroundPage />);

    await user.click(screen.getByRole('tab', { name: 'Text to speech' }));
    await user.click(screen.getByPlaceholderText('Select a model…'));
    await user.click(await screen.findByRole('option', { name: 'tts-1' }));
    await user.type(screen.getByLabelText('Text'), 'Read this aloud.');
    await user.selectOptions(screen.getByLabelText('Voice'), 'nova');
    await user.click(screen.getByRole('button', { name: 'Generate speech' }));

    const download = await screen.findByRole('link', { name: 'Download' });
    expect(download).toHaveAttribute('href', 'blob:speech');
    expect(download).toHaveAttribute('download', 'speech.mp3');
    const request = fetchMock.mock.calls.find(([url]) => url === '/openai/v1/audio/speech');
    expect(JSON.parse(String(request?.[1]?.body))).toEqual({ model: 'tts-1', input: 'Read this aloud.', voice: 'nova' });
    await waitFor(() => expect(URL.createObjectURL).toHaveBeenCalled());
  });
});