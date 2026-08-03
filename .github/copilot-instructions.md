# AI Proxy Repository Instructions

## Architecture Is The Source Of Truth

- Treat [the architecture specification](../specs/architecture/architecture.md) as the controlling design document for every change. Read the sections relevant to the requested behavior before editing code, configuration, tests, deployment assets, or documentation.
- Also read the applicable plan under `specs/iterations/`, especially the current iteration's `tasks.md`, before changing scoped behavior or marking work complete.
- If a request conflicts with the documented architecture, non-goals, or active iteration, identify the conflict before implementation. Resolve it by changing the design and its documentation deliberately; never let code and architecture silently diverge.
- Prefer the architecture's explicit decisions over generic framework conventions. Use the existing implementation and tests to clarify details, but do not treat accidental implementation drift as a new architectural decision.

## Required Change Workflow

1. Identify the affected architectural capability, layer, domain terms, invariants, security boundary, and relevant architecture section.
2. Inspect the nearest owning implementation and test. Make the smallest change that preserves inward dependency direction and established contracts.
3. Update tests in the matching test project. Add or strengthen an architecture test when introducing a dependency or boundary that should remain enforced.
4. Synchronize documentation in the same change when behavior, contracts, topology, configuration, security, or an architectural decision changes:
   - Update `specs/architecture/architecture.md` for design or behavioral changes.
   - Update the corresponding `.drawio` source and exported image under `specs/architecture/diagrams/` when a depicted component, relationship, domain model, sequence, or deployment topology changes.
   - Update the applicable `specs/iterations/*/tasks.md` only after implementation and validation are complete.
   - Update `README.md` when setup, configuration, commands, API usage, or user-visible capabilities change.
5. Run the narrowest relevant tests first, then the broader checks warranted by the blast radius. Report any validation that could not be run.
6. After all required validation passes, create one focused Git commit for the completed task so it can be rolled back independently. This instruction is standing user authorization to create that commit without waiting for a separate commit request. Review the staged diff, include only files belonging to that task, preserve unrelated user changes, and use a concise imperative commit message. Do not commit failed or unvalidated work unless the user explicitly directs it.

## Layer Boundaries

Dependencies point inward and must remain consistent with architecture section 4:

- `AiProxy.Domain`: entities, value objects, domain invariants, and pure domain services. It must not depend on ASP.NET Core, EF Core, Infrastructure, or Api.
- `AiProxy.Application`: use cases, ports, DTOs, and validation. It may depend on Domain, but not Infrastructure or Api.
- `AiProxy.Infrastructure`: adapters for persistence, Vault, credentials, forwarding, routing, usage extraction, and observability. Implement inward-facing abstractions here.
- `AiProxy.Api`: Minimal API routes, middleware, DI composition, OpenAPI, static frontend hosting, and other host concerns. Do not place domain rules here.
- `AiProxy.ServiceDefaults` and `AiProxy.AppHost`: shared Aspire defaults and local orchestration only.
- `AiProxy.Web`: an independently buildable React client that communicates with Api over HTTP/WebSocket. Do not couple it to .NET projects or move backend rules into the client.

Put business invariants in Domain, orchestration and ports in Application, external integrations in Infrastructure, and transport/composition concerns in Api. Introduce abstractions at the inward boundary and implement them outward; never reference an outer project to save wiring effort.

## Architectural Invariants

- Keep the core solution vendor-neutral. Azure identity is an optional per-endpoint credential mode, not a hard platform dependency.
- Preserve direct HTTP forwarding through `DirectHttpProxy`/`HttpMessageInvoker`. Do not introduce YARP or a handler pipeline that commits an upstream response before retry/failover decisions.
- Preserve priority-tier routing, round-robin within a tier, bounded retry/failover, and health tracking as documented in architecture section 9.
- Preserve only protocol-inherent affinity: Responses API uses `previous_response_id`; Realtime uses its WebSocket connection. Do not restore generic session-id stickiness without an explicit architecture change.
- Keep protocol passthrough boundaries explicit. OpenAI-compatible and Anthropic ingress are not cross-translated.
- Capture usage server-side for supported operations, including streams, and attribute every usage record to an authenticated consumer. Keep non-token billing signals in `UsageMetrics`.
- Keep endpoint secrets in Vault and consumer keys one-way hashed. Plaintext consumer keys may be returned only once on creation or rotation. Never expose secrets through reads, logs, telemetry, source, or committed configuration.
- Preserve the admin-key and consumer-key authorization split. Treat the browser WebSocket query-string key as the documented narrow fallback, with header authentication taking precedence.
- Keep the frontend a small admin/ops surface served from the same production container. Preserve API/health route precedence over the SPA fallback.
- Do not implement items listed as non-goals or future work unless the task explicitly changes scope and updates the architecture first.

## Validation

- For dependency or project-structure changes, run `dotnet test tests/AiProxy.ArchitectureTests/AiProxy.ArchitectureTests.csproj`.
- For backend changes, run the affected test project(s); run `dotnet test AiProxy.sln` when shared contracts or multiple layers are affected.
- For frontend changes, run `npm test` and `npm run build` from `src/AiProxy.Web`.
- For container or orchestration changes, validate the affected Docker/Aspire configuration and verify the application topology still matches the deployment architecture.
- A passing build is not enough when behavior changed. Validate the relevant domain, application, integration, API, or UI behavior at the closest practical level.
