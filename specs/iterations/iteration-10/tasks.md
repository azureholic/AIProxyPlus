# Iteration 10 — Manual Bug Hunting

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). This iteration records defects found while manually exercising the application and keeps each fix independently testable and reviewable.

## Bug 1 — Native Anthropic Chat Playground redirects to login

Observed with a Chat-capable `claude-haiku-4-5` model assigned to a native Anthropic Messages API route. Sending from Chat Playground returned the upstream `401`, which the frontend misclassified as an expired browser session and followed with an OIDC login challenge. The Playground also sent the OpenAI Chat Completions contract to the Anthropic passthrough ingress.

- [x] Reproduce the request and prove that the browser session authenticates before the upstream `401` is relayed.
- [x] Distinguish proxied consumer `401` responses from local session expiry by checking the BFF session before dispatching the session-expired event.
- [x] Select the Chat Playground ingress protocol from the chosen model's active highest-priority endpoint assignment.
- [x] Send native Anthropic Messages requests with `anthropic-version`, `max_tokens`, and Anthropic request semantics.
- [x] Render native Anthropic JSON and streaming text responses and normalize provider token usage for display.
- [x] Add regression coverage for valid/expired sessions and native Anthropic Chat Playground requests.
- [x] Run the complete frontend test suite and production build.

## Bug 2 — Replaced endpoint API key remains stale

After the native Anthropic request path was corrected, the proxy continued returning Anthropic `401` responses even
though the current Vault key succeeded against `https://api.anthropic.com/v1/messages`. The endpoint credential
provider retained the previous Vault value in its five-minute memory cache after the endpoint secret was replaced.

- [x] Verify the persisted endpoint uses base URL `https://api.anthropic.com`, native Anthropic route prefix `/v1`,
  and the expected Vault secret reference.
- [x] Validate the exact current Vault key directly against Anthropic without exposing it; the request returned `200`.
- [x] Add an Application cache-invalidation port and invoke it only after a successful endpoint secret write.
- [x] Implement invalidation in the Infrastructure credential provider while preserving normal short-TTL caching.
- [x] Add focused Application and Infrastructure regression tests for immediate key rotation.

## Bug 3 — Browser headers make native Anthropic reject proxied requests

The native Anthropic request still returned `401` after restarting with the current Vault credential. Replaying the
same successful direct request with browser header groups proved that `Origin` changes Anthropic's response to
`401 CORS requests must set 'anthropic-dangerous-direct-browser-access' header`. The proxy also forwarded session,
antiforgery, and reverse-proxy context that must remain inside its security boundary.

- [x] Compare the exact configured Vault credential and route with a direct Anthropic request; it returned `200`.
- [x] Isolate forwarded browser header groups and prove that `Origin` causes Anthropic's CORS rejection.
- [x] Strip proxy-local browser, ingress credential, antiforgery, Fetch Metadata, and reverse-proxy headers upstream.
- [x] Preserve provider protocol headers, content headers/body, and endpoint credential replacement.
- [x] Add API-to-WireMock regression coverage for the outbound header boundary.

## Bug 4 — Compressed provider responses fail in the Chat Playground

Both native Anthropic paths returned `200` from upstream, but the browser reported `ERR_CONTENT_DECODING_FAILED`.
The proxy forwarded the browser's `Accept-Encoding` upstream, then treated the compressed provider body as plain
JSON or SSE text while retaining `Content-Encoding`, producing an invalid client response. The streaming browser
abort also surfaced server-side as a `TaskCanceledException` while reading the upstream chunked stream.

- [x] Capture the browser network failure and provider response headers for streaming and non-streaming requests.
- [x] Prove native Anthropic returns a complete uncompressed response when requested with `Accept-Encoding: identity`.
- [x] Make outbound compression negotiation proxy-owned and request identity encoding from providers.
- [x] Add API-to-WireMock regression coverage for the outbound `Accept-Encoding` boundary.
- [x] Run all 333 backend tests and the frontend production build.
- [x] Validate live native Anthropic streaming and non-streaming replies with token usage in the browser.

## Bug 5 — Chat Playground message list becomes unreadable after a few turns

With OpenAI/Microsoft AI and native Anthropic replies now succeeding, the message list itself degraded after a
handful of turns: `Card` clips overflow, so once the height-capped `display:flex; flexDirection:column` list's
content exceeded that height, flexbox shrank every card toward zero instead of overflowing/scrolling — visible
as cropped, overlapping text with no working scrollbar.

- [x] Reproduce the layout failure live in the browser across a multi-turn conversation.
- [x] Replace the `Card`-based list with `flexShrink: 0` message rows and left/right-aligned chat bubbles
  (assistant left-aligned, user right-aligned).
- [x] Make the message panel a bounded, genuinely scrollable region and auto-scroll smoothly to the latest
  message on every update.
- [x] Stub jsdom's missing `Element.prototype.scrollIntoView` in the frontend test setup.
- [x] Run the complete frontend test suite and production build, then validate live across a 6-turn conversation.

## Feature — Visualize operation types and add Responses API to the Chat Playground

Manual testing surfaced two related gaps: the Usage Dashboard had no way to see request volume broken down by
`OperationType`, and the Chat Playground had no way to exercise `/openai/v1/responses` at all — only Chat
Completions and native Anthropic Messages were reachable, even though the backend has supported the Responses
API's `previous_response_id` chaining/affinity since Iteration 2.

- [x] Add a "Requests by operation type" chart to the Usage Dashboard alongside the existing tokens-by-model and
  requests-per-day charts.
- [x] Add an API selector (Chat Completions / Responses API) to the Chat Playground, hidden for Anthropic models
  since Anthropic only supports Messages via `ChatCompletion`.
- [x] Send Responses API requests as `{ model, input, stream, previous_response_id? }` — only the new turn, not
  the full transcript — and chain `previous_response_id` from each reply into the next request.
- [x] Parse both the non-streaming Responses body (`output[].content[]`) and the streaming event shape
  (`response.output_text.delta`, `response.completed`), including its own token usage.
- [x] Reset the conversation and clear `previous_response_id` when switching API mode or model, since a response
  id only resolves on the endpoint/model that created it.
- [x] Add frontend regression coverage for: the selector hidden for Anthropic, non-streaming request/response
  shape and chaining across two turns, and streaming event parsing.
- [x] Run the complete frontend test suite and production build, then validate live: a two-turn Responses API
  conversation with real `previous_response_id` chaining, and the new operation-type chart.

## Bug 6 — Native Anthropic API route is not built in

The reusable API-route migration seeded the proxy-prefixed Anthropic route but omitted the native Anthropic
`/v1` route. Administrators therefore had to create it manually, and the API Routes page labeled it Custom.

- [x] Add a stable, migration-owned Native Anthropic (`Anthropic`, `/v1`) built-in route for new databases.
- [x] Promote an existing custom Native Anthropic route in place to preserve its identifier and assignments.
- [x] Add persistence coverage for fresh seeding, in-place promotion, and downgrade behavior.
- [x] Apply the migration to the existing database and verify all five canonical routes are built in.

## Bug 7 — Foundry-hosted route names don't distinguish them from native provider routes

The Anthropic and Microsoft AI built-in route names didn't indicate they proxy through a shared Foundry endpoint,
which was easy to confuse with the Native Anthropic and Native OpenAI routes that call providers directly.

- [x] Rename the built-in `Anthropic` route to `Foundry Anthropic` and `Microsoft AI` to `Foundry Microsoft AI`.
- [x] Only rename rows still holding the original default name, preserving any administrator's own rename.
- [x] Update persistence coverage and documentation for the new built-in names.

## Bug 8 — Responses API selector shows before a model is selected

`provider` defaulted to `'OpenAI'` before any model was chosen, so the Chat Playground's API selector (and its
default "Chat Completions" value) rendered even with no model selected, implying an OpenAI model was already active.

- [x] Only show the API selector once a model is selected, in addition to the existing Anthropic exclusion.
- [x] Add frontend regression coverage for the selector being hidden before any model is selected.
- [x] Run the complete frontend test suite and production build.

## Feature — Distinguish Anthropic's native Messages API from ChatCompletion

The Usage Dashboard reported native Anthropic Messages requests as `ChatCompletion`, the same operation type as
OpenAI-style chat completions, making it impossible to tell the two protocols apart from usage data alone.

- [x] Add a dedicated `MessagesApi` operation type instead of reusing `ChatCompletion` for Anthropic.
- [x] Update the Anthropic usage extractor, upstream route resolver, and `/anthropic/v1/messages` ingress to use it.
- [x] Add a migration backfilling historical `usage_records` rows from `ChatCompletion` to `MessagesApi` for
  `Provider = Anthropic`, reversible on `Down`.
- [x] Update the domain model diagram, architecture docs, and backend/frontend tests for the new operation type.
- [x] Run the complete backend and frontend test suites and the frontend production build.

## Feature — Show which endpoint served each usage record

The Usage Dashboard had no way to tell which of a model's assigned endpoints actually served a given request,
only the model and consumer, making it impossible to attribute usage or diagnose a bad endpoint from the table.

- [x] Add a denormalized `EndpointName` field to `UsageRecord`, captured at write time like `ConsumerName`.
- [x] Populate it from the serving `ModelEndpoint` assignment's name when a request completes.
- [x] Add a migration backfilling historical `usage_records` rows from the `endpoints` table by `EndpointId`,
  falling back to `'unknown'` for rows whose endpoint no longer exists; column drops on `Down`.
- [x] Add an "Endpoint" column to the Usage Dashboard table and update the architecture schema docs.
- [x] Run the complete backend and frontend test suites and the frontend production build.

## Feature — Rename Third Party OpenAI/Anthropic routes to reflect non-Foundry backends

The built-in `Foundry OpenAI` and `Foundry Anthropic` routes implied their shared endpoint is always Azure AI
Foundry, but the same OpenAI-/Anthropic-compatible API shape is also served by other third-party backends such
as Amazon Bedrock. `Foundry Microsoft AI` is unaffected — Microsoft AI is only ever reached through Foundry.

- [x] Rename the built-in `Foundry OpenAI` route to `Third Party OpenAI` and `Foundry Anthropic` to
  `Third Party Anthropic`, migration-owned and applied only to rows still holding the original default name.
- [x] Add persistence coverage proving the rename applies on upgrade, preserves an administrator's own rename,
  and reverts on downgrade.
- [x] Update architecture docs, README, and backend/frontend tests referencing the renamed built-ins.
- [x] Run the complete backend and frontend test suites and the frontend production build.

