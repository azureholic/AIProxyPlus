# Iteration 8 — Realtime Voice Input and Output

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). This iteration turns the
Realtime Voice Playground from a raw-event connectivity tool into a usable microphone and speaker experience.

## Phase 1 — Browser audio client

- [x] Capture microphone audio in an `AudioWorklet`, resample to mono PCM16 at 24 kHz, and send base64 Realtime
  `input_audio_buffer.append` events while push-to-talk is active.
- [x] Commit each captured turn and request a response when push-to-talk is released.
- [x] Decode and schedule incoming Realtime audio deltas for gap-free speaker playback.
- [x] Keep raw event send/log diagnostics available without making them the primary workflow.
- [x] Stop media tracks, worklets, contexts, queued playback, and the WebSocket on disconnect and unmount.
- [x] Surface microphone permission, unsupported-browser, malformed-event, and socket failures in the UI.

## Phase 2 — Validation and documentation

- [x] Add focused tests for connection setup, recording lifecycle, outbound audio events, inbound playback, and
  cleanup.
- [x] Run frontend tests and the production build.
- [x] Update README Voice Playground usage and browser requirements.
- [ ] Validate microphone input and speaker output in a Chromium browser against a configured Realtime model.
