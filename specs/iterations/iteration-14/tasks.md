# Iteration 14 — Manual-Test Bug Hunt

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). This iteration captures
defects found during manual testing and keeps each repair independently reproducible and validated.

## Bug 1 — Local Vault loses external-provider API keys

### Reproduction and root cause

- [x] Confirm an endpoint secret was written successfully, then could not be resolved after a later Docker restart.
- [x] Identify that both Aspire and Compose mounted `vault-data` while `VAULT_DEV_*` forced Vault's in-memory
  backend, leaving the volume unused.

### Repair

- [x] Replace local dev mode with a pinned, file-backed Vault and idempotent initialization/unseal bootstrap.
- [x] Restrict the fixed local token to KV v2 paths under `secret/ai-proxy/*` instead of granting root access.
- [x] Make the API wait for Vault to be initialized and unsealed in both Aspire and Compose.
- [x] Synchronize architecture, deployment diagram, and local-run documentation.

### Validation

- [x] Store a fake endpoint secret, restart the Vault container, and resolve the same value from the persisted volume.
- [x] Run the architecture regression test and build the AppHost.
- [ ] Re-enter any external-provider keys already lost by the old in-memory Vault and confirm live OpenAI/Anthropic calls.

## Bug 2 — Voice Playground WebSocket fails over HTTP/2

### Reproduction and root cause

- [x] Reproduce the Voice Playground failure in Chromium: ticket issuance succeeds, then the socket closes with
  code `1006`.
- [x] Confirm the browser negotiates HTTP/2 and sends an extended `CONNECT /openai/v1/realtime`, while the Minimal
  API route accepts only `GET` and returns `405` before authentication or proxy routing runs.

### Repair

- [x] Map both HTTP/1.1 `GET` and HTTP/2 `CONNECT` to the same authenticated Realtime handler.
- [x] Synchronize the documented Realtime ingress contract.

### Validation

- [x] Add and run a regression test proving `CONNECT` reaches Realtime routing instead of returning `405`.
- [x] Confirm the Voice Playground connects to the configured live OpenAI Realtime model in Chromium.

## Bug 3 — Realtime Voice usage is not reported

### Reproduction and root cause

- [x] Complete a successful live Voice Playground response and confirm that no usage record appears.
- [x] Identify that `RealtimeProxyService` forwards backend frames without observing OpenAI's terminal
  `response.done.response.usage` object.

### Repair

- [x] Pass the authenticated consumer id into Realtime routing and preserve the existing consumer name attribution.
- [x] Passively assemble backend text-message fragments while forwarding every fragment unchanged.
- [x] Persist one `RealtimeVoice` usage record per unique usage-bearing `response.done`, grouped by connection id.
- [x] Ignore duplicate ids, malformed JSON, and events without authoritative usage without affecting the socket.
- [x] Synchronize the architecture's Realtime and usage-capture contracts.

### Validation

- [x] Prove fragmented `response.done` forwarding remains byte-for-byte equivalent and captures token totals with
  consumer, model, provider, and endpoint attribution.
- [x] Prove multiple responses on one connection create separate records with one session id while duplicates and
  invalid events create no records.
- [x] Confirm a live Voice Playground response appears in the Usage dashboard.
