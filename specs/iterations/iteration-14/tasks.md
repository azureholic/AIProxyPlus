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