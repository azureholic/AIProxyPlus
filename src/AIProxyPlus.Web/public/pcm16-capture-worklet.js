class Pcm16CaptureProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.targetSampleRate = 24000;
    this.ratio = sampleRate / this.targetSampleRate;
    this.sourcePosition = 0;
    this.samples = [];
  }

  process(inputs) {
    const channel = inputs[0]?.[0];
    if (!channel) {
      return true;
    }

    while (this.sourcePosition < channel.length) {
      const sample = channel[Math.floor(this.sourcePosition)];
      const clamped = Math.max(-1, Math.min(1, sample));
      this.samples.push(clamped < 0 ? clamped * 0x8000 : clamped * 0x7fff);
      this.sourcePosition += this.ratio;
    }
    this.sourcePosition -= channel.length;

    if (this.samples.length >= 2048) {
      const pcm = Int16Array.from(this.samples.splice(0, 2048));
      this.port.postMessage(pcm.buffer, [pcm.buffer]);
    }

    return true;
  }
}

registerProcessor('pcm16-capture', Pcm16CaptureProcessor);
