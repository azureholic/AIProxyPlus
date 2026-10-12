import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { encodePcm16, getAudioDelta, RealtimeAudioSession } from '../pages/playground/realtimeAudio';

class AudioWorkletNodeStub {
  port = { onmessage: null as ((event: MessageEvent<ArrayBuffer>) => void) | null, close: vi.fn() };
  disconnect = vi.fn();
  constructor() {
    latestWorklet = this;
  }
}

class AudioContextStub {
  currentTime = 0;
  destination = {} as AudioDestinationNode;
  audioWorklet = { addModule: vi.fn().mockResolvedValue(undefined) };
  resume = vi.fn().mockResolvedValue(undefined);
  close = vi.fn().mockResolvedValue(undefined);
  createMediaStreamSource = vi.fn(() => ({ connect: vi.fn(), disconnect: vi.fn() }));
  createBuffer = vi.fn(() => ({ duration: 0.1, copyToChannel: vi.fn() }));
  createBufferSource = vi.fn(() => ({ connect: vi.fn(), start: vi.fn(), stop: vi.fn(), onended: null }));
}

let latestWorklet: AudioWorkletNodeStub;

function socket() {
  return { readyState: WebSocket.OPEN, send: vi.fn() };
}

describe('RealtimeAudioSession', () => {
  const track = { stop: vi.fn() };

  beforeEach(() => {
    vi.stubGlobal('AudioContext', AudioContextStub);
    vi.stubGlobal('AudioWorkletNode', AudioWorkletNodeStub);
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [track] }) },
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.clearAllMocks();
  });

  it('configures PCM16 full-duplex audio', () => {
    const realtimeSocket = socket();
    new RealtimeAudioSession(realtimeSocket).configure();

    expect(JSON.parse(realtimeSocket.send.mock.calls[0][0])).toEqual({
      type: 'session.update',
      session: {
        modalities: ['text', 'audio'],
        input_audio_format: 'pcm16',
        output_audio_format: 'pcm16',
        turn_detection: null,
      },
    });
  });

  it('sends captured PCM16 frames and commits the turn', async () => {
    const realtimeSocket = socket();
    const session = new RealtimeAudioSession(realtimeSocket);

    expect(await session.startCapture()).toBe(true);
    const frame = new Int16Array([0, 32767, -32768]).buffer;
    latestWorklet.port.onmessage?.({ data: frame } as MessageEvent<ArrayBuffer>);
    await session.stopCapture(true);

    expect(JSON.parse(realtimeSocket.send.mock.calls[0][0])).toEqual({
      type: 'input_audio_buffer.append',
      audio: encodePcm16(frame),
    });
    expect(realtimeSocket.send).toHaveBeenNthCalledWith(2, JSON.stringify({ type: 'input_audio_buffer.commit' }));
    expect(realtimeSocket.send).toHaveBeenNthCalledWith(3, JSON.stringify({ type: 'response.create' }));
    expect(track.stop).toHaveBeenCalled();
  });

  it('recognizes supported server audio delta event names', () => {
    expect(getAudioDelta({ type: 'response.audio.delta', delta: 'first' })).toBe('first');
    expect(getAudioDelta({ type: 'response.output_audio.delta', delta: 'second' })).toBe('second');
    expect(getAudioDelta({ type: 'response.text.delta', delta: 'ignored' })).toBeNull();
  });
});
