# Iteration 11 — GPT Image Model Token Usage

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). This iteration extracts real
token usage for GPT Image-style models (OpenAI's `gpt-image-2` and newer, Microsoft AI's `MAI-Image-2.5-Flash`
and newer), which report a token-based `usage` object, instead of treating every Images API response as
token-less. DALL-E is deprecated and is out of scope for live validation — see Phase 3.

## Background

- `ImageUsageExtractor` (`src/AiProxy.Infrastructure/Usage/ImageUsageExtractor.cs`) currently only reads
  `data` (image count), `size`, and `quality` into `UsageRecord.UsageMetrics`; `PromptTokens`/`CompletionTokens`/
  `TotalTokens` are always `null` for `ImageGeneration`/`ImageEdit`/`ImageVariation`, and
  `TryExtractFromStreamingEvent` always returns `null`. Architecture §6.3 documents this as "most Images API
  responses don't report token usage," which is now only true for legacy, non-token image models (e.g. the
  deprecated DALL-E line) rather than current GPT Image-style models.
- Per OpenAI's current API reference, GPT Image models return a `usage` object
  (`input_tokens`/`output_tokens`/`total_tokens`, each broken down by `input_tokens_details`/
  `output_tokens_details` text vs. image tokens) on:
  - the non-streaming JSON body of `POST /images/generations` and `POST /images/edits`;
  - the terminal `image_generation.completed` / `image_edit.completed` SSE event when the client requests
    `stream: true` (only supported for GPT Image-style models; legacy DALL-E-style models reject
    `stream: true` outright, so the proxy needs no extra validation).
  - `POST /images/variations` (DALL-E-only, deprecated) never returns `usage` and is unaffected.
- Our own catalog already has two `ImageGeneration` models to validate against: `gpt-image-2`
  (`Provider.OpenAI`) and `MAI-Image-2.5-Flash` (`Provider.MicrosoftAI`), both currently healthy with one
  assigned endpoint each — see Phase 3. DALL-E is deprecated and intentionally excluded from live testing;
  the unit-test fixtures for the no-`usage` fallback path (Phase 2) use a generic legacy-shaped response rather
  than a DALL-E-specific example.
- Streaming for Images ingress is **already mechanically generic**: `ProxyRoutingService` decides SSE vs.
  buffered forwarding purely from the request body's `stream` field (`RequestBodyPeek.ExtractFields`), with no
  per-`OperationType` gate, and `SseStreamRelay` calls whichever `IUsageExtractor` is resolved for the operation.
  Architecture §6.3's "Image requests are treated as non-streaming request/response for Iteration 1" note is a
  **documented design simplification**, not an enforced restriction — a client already reaches the generic SSE
  relay today if it sends `stream: true` to an image ingress route; usage capture for that path just silently
  returns nothing today because `TryExtractFromStreamingEvent` is a no-op. This iteration turns that on
  intentionally for image completed-events, superseding the §6.3 note.
- No new `OperationType`, ingress route, `Provider`, or outbound request mutation is needed:
  `ImageUsageExtractor` is already registered for both `Provider.OpenAI` and `Provider.MicrosoftAI` across all
  three image `OperationType` values (`Program.cs`), and `OutboundBodyMutator.EnsureStreamUsageRequested` only
  applies to `ChatCompletion`/`Completion` — GPT Image models report `usage` on the completed frame
  unconditionally, exactly like the Responses API's `response.completed`, so no `stream_options`-style
  injection is required.

## Proposed design

- `ImageUsageExtractor.ExtractFromResponseBody` additionally reads an optional top-level `usage` object and maps
  `input_tokens` → `PromptTokens`, `output_tokens` → `CompletionTokens`, `total_tokens` → `TotalTokens`, in
  addition to (not instead of) the existing `image_count`/`size`/`quality` `UsageMetrics` — `UsageExtractionResult`
  already allows both token fields and `UsageMetrics` to be populated together, so no data is lost when a GPT
  Image model reports both signals. When `usage` is absent (legacy/deprecated image models, or
  `/images/variations`), behavior is unchanged from today.
- `ImageUsageExtractor.TryExtractFromStreamingEvent` mirrors `ResponsesUsageExtractor`'s pattern: parse the SSE
  data payload as JSON, check `type` for `image_generation.completed` or `image_edit.completed`, and extract the
  same `usage` mapping from that event. Any other event `type` (e.g. `*.partial_image`) returns `null` and is
  passed through untouched.
- `IsUsageOnlyStreamingEvent` is **not** overridden (stays the inherited `false` default): unlike OpenAI chat
  completions' dedicated trailing usage-only chunk, the image `*.completed` event also carries the final
  `b64_json` image data, so it must never be suppressed from the client-facing stream even when the client
  didn't explicitly request usage. Add a regression test asserting this explicitly so a future change can't
  silently start dropping the final image frame.
- Remove/replace the now-inaccurate `TryExtractFromStreamingEvent_AlwaysNull_SinceImagesApiIsNeverStreamed` test.
- No changes to `Program.cs` extractor registration, `UpstreamRouteResolver`, ingress route mapping, or
  `OutboundBodyMutator` — this is purely an `IUsageExtractor` implementation change plus documentation, per the
  pluggability architecture already documented in §10.

## Phase 1 — Architecture specification

- [x] Update architecture §6.3 to state that GPT Image-style models report token usage (non-streaming and via
  the terminal `*.completed` SSE event) while deprecated/legacy image models and `/images/variations` remain
  metrics-only, and remove/qualify the superseded "Images requests are treated as non-streaming ... for
  Iteration 1" claim.
- [x] Update architecture §10 (Streaming & Usage Capture) to add the image `*.completed` event alongside the
  existing chat completions/Responses API/Anthropic bullets as another self-contained, non-suppressible
  usage-bearing streaming event.
- [x] Update the §12 API surface table row for "OpenAI ingress (images)" so it no longer states "non-streaming"
  unconditionally, and instead notes GPT Image models support `stream: true` with usage captured from the
  completed event.
- [x] Confirm no diagram changes are required (no new component, route, operation type, or sequence — only an
  existing extractor implementation gains a new optional field it reads) and record that explicitly in this
  file's Phase 3 sign-off rather than editing `specs/architecture/diagrams/*`.

## Phase 2 — Usage extraction implementation

- [x] Extend `ImageUsageExtractor.ExtractFromResponseBody` to parse the optional `usage` object into
  `PromptTokens`/`CompletionTokens`/`TotalTokens` alongside the existing `UsageMetrics` mapping.
- [x] Implement `ImageUsageExtractor.TryExtractFromStreamingEvent` for `image_generation.completed` and
  `image_edit.completed` event types, returning `null` for every other event type/shape.
- [x] Leave `IsUsageOnlyStreamingEvent` on the interface default (`false`) for this extractor and add a test
  proving it, so the completed event is never suppressed from the client stream.
- [x] Update `ImageUsageExtractorTests` — add cases for: GPT Image non-streaming response with `usage` (tokens
  + metrics both populated), a legacy-shaped response without `usage` (unchanged, metrics-only — no DALL-E-
  specific naming needed since it's deprecated), the `image_generation.completed`/`image_edit.completed`
  streaming cases, a `*.partial_image` event returning `null`, and remove the now-false "images are never
  streamed" assumption.
- [x] Add an API-level regression test (mirroring `UsageCaptureTests`) proving an end-to-end streaming image
  generation request captures token usage from the `*.completed` SSE event without altering the client-visible
  image bytes.

## Phase 3 — Validation

- [x] Run the complete backend test suite (`dotnet test AiProxy.sln`) — 348 tests passed.
- [x] Manually validate live against `gpt-image-2` (`Provider.OpenAI`, Azure AI Foundry-hosted) with one
  non-streaming and one streaming request: `usage_records` captured `PromptTokens`/`CompletionTokens`/
  `TotalTokens` (e.g. 13/229/242 non-streaming, 14/186/200 streaming from the `image_generation.completed`
  event), and the Usage Dashboard's "Tokens" column showed the value instead of `—` for those rows.
- [x] Attempted the same live check against `MAI-Image-2.5-Flash` (`Provider.MicrosoftAI`): the request
  succeeded (`200`) and metrics-only capture worked correctly (image count/size), but this deployment's
  response did not include a `usage` object, so no tokens were reported — this is an upstream/environment
  characteristic (this Foundry-hosted Microsoft AI image deployment doesn't yet emit token usage), not an
  extractor defect; the fallback-to-metrics-only path is exactly the designed behavior and is what was
  observed.
- [x] DALL-E is deprecated — no live validation against it is planned; the Phase 2 unit tests cover the
  no-`usage` fallback path instead.
