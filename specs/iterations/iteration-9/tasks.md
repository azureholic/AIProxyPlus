# Iteration 9 — Model Capabilities and Speech Playground

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). This iteration adds
administrator-managed model capabilities, uses them to keep each Playground model selector relevant, and adds
request/response speech-to-text and text-to-speech workflows alongside the existing Realtime Voice experience.

## Proposed capability model

- A model has one or more capabilities. Multi-purpose models may declare several capabilities.
- Use five explicit capability values: `Chat`, `ImageGeneration`, `Realtime`, `SpeechToText`, and `TextToSpeech`.
  The two speech values remain separate because a transcription model is not necessarily able to synthesize audio,
  or vice versa.
- Capabilities are administrator-managed discovery metadata. They filter Playground model selectors but do not
  replace the provider/operation compatibility checks in routing or block existing non-Playground operations such
  as completions, embeddings, image edits, image variations, or Responses API calls.
- Existing models are backfilled with `Chat`, `ImageGeneration`, and `Realtime` to preserve current Playground
  visibility after migration. Administrators can then narrow each model to its actual capabilities. Existing models
  do not receive either speech capability automatically.
- Keep Realtime Voice and request/response Speech as separate Playground experiences. Realtime continues to use the
  WebSocket session; Speech uses HTTP transcription and synthesis endpoints.

## Phase 1 — Architecture specification

- [x] Update the domain model and frontend sections of the architecture specification with the capability vocabulary,
  multi-capability invariant, persistence shape, migration behavior, and Playground filtering rules.
- [x] Define capabilities as discovery metadata rather than a complete operation-support matrix, and document why
  ingress routing continues to rely on provider and `OperationType` compatibility.
- [x] Move request/response speech-to-text and text-to-speech out of non-goals and define their HTTP ingress,
  authentication, payload passthrough, retry, usage, and observability boundaries.
- [x] Specify multipart model resolution for transcription uploads without changing file bytes, content type, or
  boundary, and define bounded request-size behavior for recorded and uploaded audio.
- [x] Specify binary audio response passthrough for synthesis, including content type, content length when available,
  cancellation, and disposal behavior.
- [x] Update the domain, request-routing, and frontend diagrams and export their rendered images before implementation.

## Phase 2 — Domain and persistence

- [x] Add a closed `ModelCapability` enum with `Chat`, `ImageGeneration`, `Realtime`, `SpeechToText`, and
  `TextToSpeech` values.
- [x] Extend the `Model` aggregate with a non-empty, duplicate-free capability set and explicit replacement/update
  behavior that preserves the aggregate's timestamp semantics.
- [x] Persist capabilities in a model-owned `model_capabilities` relation with a unique `(model_id, capability)` key
  and cascading deletion with the owning model.
- [x] Add a migration that creates capability persistence and backfills every existing model with `Chat`,
  `ImageGeneration`, and `Realtime` without assigning either speech capability.
- [x] Ensure repositories load capabilities with models without changing endpoint-assignment ownership or routing
  queries.
- [ ] Add domain, EF configuration, migration, and repository tests for validation, multi-capability models,
  duplicate prevention, replacement, backfill, loading, and deletion.

## Phase 3 — Application and configuration API

- [x] Extend model create and update commands, validators, handlers, and responses to require and expose capabilities.
- [x] Reject missing, empty, duplicate, and unknown capability values with the existing validation error contract.
- [x] Keep the model list and detail endpoints backward-compatible apart from the additive `capabilities` field.
- [ ] Update OpenAPI metadata and examples for model capability configuration.
- [ ] Add application and API tests for create, update, list, detail, serialization, validation failures, and
  persistence of multiple capabilities.

## Phase 4 — Speech ingress and upstream routing

- [x] Add distinct `OperationType` values for speech-to-text and text-to-speech so routing, telemetry, and usage can
  distinguish transcription from synthesis.
- [x] Add authenticated OpenAI-compatible `POST /openai/v1/audio/transcriptions` and
  `POST /openai/v1/audio/speech` ingress routes.
- [x] Resolve the transcription model from the multipart `model` form field while preserving the original multipart
  payload for forwarding; return the established local error shape when the field is absent, malformed, or oversized.
- [x] Resolve the synthesis model from the JSON `model` field through the existing buffered request path and relay the
  upstream audio body without JSON or SSE assumptions.
- [x] Extend provider adapters and upstream route resolution for `/audio/transcriptions` and `/audio/speech` only for
  providers that implement the compatible API surface; reject unsupported provider/operation combinations before
  credentials are resolved or upstream traffic is sent.
- [x] Preserve consumer attribution, credential placement, timeout, bounded retry/failover, endpoint health tracking,
  cancellation, request identifiers, and protocol passthrough for both operations.
- [x] Capture provider-reported token or audio usage when available without inventing billing values; document and test
  the behavior when an upstream speech response contains no usable usage signal.
- [ ] Add focused routing and integration tests for multipart forwarding, binary audio responses, provider route
  composition, authentication, failover, malformed requests, unsupported combinations, and non-speech regressions.

## Phase 5 — Model configuration and filtered selectors

- [x] Add a capability multi-select to model creation and editing, require at least one selection, and display
  capability badges in model list/detail views.
- [x] Extend the frontend model API types and requests with the additive capability collection.
- [x] Change the shared `ModelPicker` to require a capability and render only matching models.
- [x] Pass `Chat`, `ImageGeneration`, and `Realtime` from the existing Chat, Images, and Voice Playgrounds
  respectively.
- [x] Clear an existing selection if refreshed model data no longer contains that model or the model no longer matches
  the requested capability.
- [x] Show a useful empty state when no configured model supports the current Playground capability.
- [x] Add frontend API-contract and interaction tests for capability editing, badges, selector filtering, stale
  selection cleanup, and empty states.

## Phase 6 — Speech Playground

- [x] Add a Speech Playground route and navigation item with a segmented speech-to-text/text-to-speech mode control;
  keep the existing Voice item clearly labeled as the Realtime experience.
- [x] In speech-to-text mode, filter models by `SpeechToText`, let the user record from the microphone or select a local
  audio file, preview the chosen recording, submit it as multipart form data, and render the returned transcription.
- [x] Request microphone permission only when recording starts; expose recording state and elapsed time; stop every
  media track on stop, replacement, navigation, failure, and unmount.
- [x] Validate supported file types and the architecture-defined size limit before upload, while treating the selected
  or recorded file as ephemeral browser state that is never persisted by AI Proxy.
- [x] In text-to-speech mode, filter models by `TextToSpeech`, accept text and the required voice setting, request audio,
  and provide native playback plus a download action using the upstream response content type.
- [x] Revoke replaced and unmounted object URLs, cancel in-flight requests on mode changes/navigation, and surface
  permission, recording, validation, upstream, decoding, and playback failures through the existing error UI.
- [ ] Add focused component tests for mode-specific filtering, recording lifecycle and cleanup, upload submission,
  transcription rendering, synthesis request shape, audio playback/download, cancellation, and error states.

## Phase 7 — Documentation and validation

- [x] Update README model configuration and Playground guidance with capability examples, Speech workflows, supported
  browser APIs, upload limits, and the distinction between Speech and Realtime Voice.
- [x] Run focused domain, application, infrastructure, API, architecture, and frontend tests after each phase.
- [ ] Run `dotnet test AiProxy.sln`, then `npm test` and `npm run build` from `src/AiProxy.Web`.
- [ ] Validate live model filtering for Chat, Images, Voice, speech-to-text, and text-to-speech in Chromium.
- [ ] Validate microphone transcription, local-file transcription, synthesized playback, and synthesized download
  against configured speech models without exposing audio or credentials in logs or telemetry.
