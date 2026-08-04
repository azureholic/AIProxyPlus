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
