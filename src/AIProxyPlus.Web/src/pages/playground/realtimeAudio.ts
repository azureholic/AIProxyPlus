const sampleRate = 24000;

export type RealtimeSocket = Pick<WebSocket, 'readyState' | 'send'>;

export function encodePcm16(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
  }
  return btoa(binary);
}

function decodePcm16(base64: string): Float32Array<ArrayBuffer> {
  const binary = atob(base64);
  const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
  const view = new DataView(bytes.buffer);
  const samples = new Float32Array(new ArrayBuffer(Math.floor(bytes.length / 2) * Float32Array.BYTES_PER_ELEMENT));
  for (let index = 0; index < samples.length; index += 1) {
    samples[index] = view.getInt16(index * 2, true) / 0x8000;
  }
  return samples;
}

export class RealtimeAudioSession {
  private readonly socket: RealtimeSocket;
  private captureContext: AudioContext | null = null;
  private playbackContext: AudioContext | null = null;
  private stream: MediaStream | null = null;
  private source: MediaStreamAudioSourceNode | null = null;
  private worklet: AudioWorkletNode | null = null;
  private playbackSources = new Set<AudioBufferSourceNode>();
  private nextPlaybackTime = 0;
  private captureRequested = false;

  constructor(socket: RealtimeSocket) {
    this.socket = socket;
  }

  configure(): void {
    if (this.socket.readyState !== WebSocket.OPEN) {
      return;
    }
    this.socket.send(JSON.stringify({
      type: 'session.update',
      session: {
        modalities: ['text', 'audio'],
        input_audio_format: 'pcm16',
        output_audio_format: 'pcm16',
        turn_detection: null,
      },
    }));
  }

  async startCapture(): Promise<boolean> {
    if (this.worklet) {
      return true;
    }
    if (!navigator.mediaDevices?.getUserMedia || !window.AudioWorkletNode) {
      throw new Error('This browser does not support microphone audio capture.');
    }

    this.captureRequested = true;
    const stream = await navigator.mediaDevices.getUserMedia({
      audio: {
        channelCount: 1,
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true,
      },
    });

    if (!this.captureRequested) {
      stream.getTracks().forEach((track) => track.stop());
      return false;
    }

    try {
      const context = new AudioContext();
      await context.audioWorklet.addModule('/pcm16-capture-worklet.js');
      await context.resume();
      const source = context.createMediaStreamSource(stream);
      const worklet = new AudioWorkletNode(context, 'pcm16-capture', {
        numberOfInputs: 1,
        numberOfOutputs: 0,
        channelCount: 1,
      });
      worklet.port.onmessage = ({ data }: MessageEvent<ArrayBuffer>) => {
        if (this.socket.readyState === WebSocket.OPEN) {
          this.socket.send(JSON.stringify({ type: 'input_audio_buffer.append', audio: encodePcm16(data) }));
        }
      };
      source.connect(worklet);

      this.stream = stream;
      this.captureContext = context;
      this.source = source;
      this.worklet = worklet;
      return true;
    } catch (error) {
      stream.getTracks().forEach((track) => track.stop());
      throw error;
    }
  }

  async stopCapture(commit: boolean): Promise<void> {
    this.captureRequested = false;
    this.worklet?.port.close();
    this.worklet?.disconnect();
    this.source?.disconnect();
    this.stream?.getTracks().forEach((track) => track.stop());
    await this.captureContext?.close();

    this.worklet = null;
    this.source = null;
    this.stream = null;
    this.captureContext = null;

    if (commit && this.socket.readyState === WebSocket.OPEN) {
      this.socket.send(JSON.stringify({ type: 'input_audio_buffer.commit' }));
      this.socket.send(JSON.stringify({ type: 'response.create' }));
    }
  }

  async play(base64: string): Promise<void> {
    const context = this.playbackContext ?? new AudioContext({ sampleRate });
    this.playbackContext = context;
    await context.resume();

    const samples = decodePcm16(base64);
    const buffer = context.createBuffer(1, samples.length, sampleRate);
    buffer.copyToChannel(samples, 0);
    const source = context.createBufferSource();
    source.buffer = buffer;
    source.connect(context.destination);

    const startTime = Math.max(context.currentTime, this.nextPlaybackTime);
    source.start(startTime);
    this.nextPlaybackTime = startTime + buffer.duration;
    this.playbackSources.add(source);
    source.onended = () => this.playbackSources.delete(source);
  }

  async close(): Promise<void> {
    await this.stopCapture(false);
    for (const source of this.playbackSources) {
      source.stop();
    }
    this.playbackSources.clear();
    await this.playbackContext?.close();
    this.playbackContext = null;
    this.nextPlaybackTime = 0;
  }
}

export function getAudioDelta(event: unknown): string | null {
  if (!event || typeof event !== 'object') {
    return null;
  }
  const message = event as { type?: unknown; delta?: unknown };
  const isAudioDelta = message.type === 'response.audio.delta' || message.type === 'response.output_audio.delta';
  return isAudioDelta && typeof message.delta === 'string' ? message.delta : null;
}
