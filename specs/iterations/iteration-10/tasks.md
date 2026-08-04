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
