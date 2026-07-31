# Iteration 3 — Tasks

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md), which is updated in
Phase 6 to describe these changes (not left as an Iteration-1/2-only description).

## Motivation (from planning discussion)

The proxy has been API-only through Iterations 1–2 — every configuration change (model deployments,
endpoints) and every diagnostic query (usage) requires hand-crafted `curl`/Postman calls against
`/api/v1/config/*` and `/api/v1/usage`, and there is no way to exercise the ingress routes (chat, images,
realtime) without a separate client. Iteration 3 adds a small admin/ops **frontend** — served from the *same*
container as the API (no separate frontend host/deployment) — covering:

1. **Config**: model deployments + their endpoints (consumers get a minimal CRUD surface too, purely as
   supporting infrastructure — see Decisions).
2. **Playground**: manual exercising of the ingress routes — chat (streaming + non-streaming), image
   generation, and a basic Realtime (voice) connectivity test.
3. **Dashboard**: `usage_records` query/visualization for chargeback review.

## Decisions locked in for this iteration

- **Stack**: Vite + React + TypeScript. Vite over CRA (unmaintained) or Next.js (its SSR/server-routing model
  doesn't fit "static files served by ASP.NET" — plain client-side React is all this needs).
- **UI library**: FluentUI v9 (`@fluentui/react-components` + `@fluentui/react-icons`), per the request. Left
  nav via FluentUI's `Nav`/`Drawer` components, not a hand-rolled sidebar.
- **Routing**: `react-router-dom` (client-side, hash- or browser-history-based — browser-history chosen, which
  is exactly why the backend needs a SPA fallback route, see Phase 2).
- **Data fetching**: a small hand-written `fetch` wrapper (adds the right auth header per call, parses JSON,
  surfaces `RoutingFailure`-style `{ error }` bodies as thrown errors). No TanStack Query/SWR/Redux — the
  admin surface's data volume and interaction complexity doesn't justify another dependency; revisit if the
  dashboard/playground grow enough to need real caching/invalidation.
- **Charting**: `recharts` for the usage dashboard (tokens-by-deployment bar chart, usage-over-time line
  chart) — the smallest well-maintained React charting library that doesn't require a second bespoke SVG layer.
- **Auth (frontend-side)**: there is no user-facing auth system in this codebase yet (architecture.md §17 —
  admin auth is a placeholder static key). The frontend gets a **Settings** page where an operator pastes the
  **Admin API Key** (`X-Admin-Api-Key`, for Config + Usage) and picks/creates a **Consumer API Key**
  (`X-Api-Key`, for Playground calls); both are kept in `sessionStorage` only (cleared when the tab closes),
  never `localStorage`, and never logged. This is explicitly a stand-in, matching the same "placeholder auth"
  posture the backend itself already documents — not a new security model.
- **Consumers page**: not explicitly requested, but the Playground is unusable without a valid Consumer API
  key (ingress routes require one — architecture.md §13), and the only way to obtain one is
  `POST /api/v1/config/consumers`. Added as a minimal page (list/create/rotate/revoke) purely as the supporting
  infrastructure the Playground needs, not as a first-class requested feature — kept intentionally small.
- **Single-container serving pattern**: Vite's build output (`npm run build`) is written *directly* into
  `src/AiProxy.Api/wwwroot` (via `build.outDir`), exactly like copying static files into the ASP.NET project
  per the requested pattern. `wwwroot/` is gitignored (contents only, like `logs/`/`test-results/`) since it's
  a build artifact, not source. `AiProxy.Api` serves it via `UseDefaultFiles`/`UseStaticFiles` +
  `MapFallbackToFile("index.html")` for client-side routing, registered so it never shadows the existing
  `/openai/*`, `/anthropic/*`, `/api/*`, `/health/*`, `/openapi/*`/`/scalar` routes (those are all more
  specific than the fallback and win regardless of registration order under endpoint routing — verified, not
  assumed, during Phase 2 implementation).
- **Local frontend dev workflow**: `npm run dev` (Vite dev server, hot reload) with a `vite.config.ts` proxy
  forwarding `/api`, `/openai`, `/anthropic`, `/health` to the API's `launchSettings.json` dev port, so
  frontend iteration doesn't require a full `npm run build` + restart loop. This is a dev convenience only —
  the production/container path is always "build once, serve as static files" per the requested pattern.
- **Voice/Realtime Playground scope — intentionally reduced**: full duplex mic capture + PCM16 encode/decode
  + playback against OpenAI's Realtime protocol is a substantial standalone effort (Web Audio API worklets,
  base64 frame chunking, barge-in handling). Iteration 3 ships a **connectivity + text-event test tool**
  instead: connect/disconnect to `/openai/v1/realtime?model=...`, send a raw JSON event (e.g. a
  `conversation.item.create` text message + `response.create`), and show the raw event log received back.
  This proves out the WebSocket proxy end-to-end from a browser without committing to full audio UX in this
  iteration — real mic/speaker support is called out as a follow-up, not silently dropped.
- **Frontend testing**: `vitest` + React Testing Library, but scoped to a handful of smoke tests (app shell
  renders, nav routes to each top-level page, the API client attaches the right header and surfaces error
  bodies) — not full coverage of every page/interaction. This matches the project's existing "meaningful
  coverage over raw percentage" testing philosophy (architecture.md §15) applied to a UI project instead of
  a business-logic-heavy backend one.
- **No new backend endpoints**: everything the frontend needs already exists on the API surface (§7.2/§12 of
  architecture.md). Iteration 3 is frontend-only work plus two small, anticipated Infrastructure exceptions:
  the Program.cs static-file/SPA-fallback wiring (Phase 2), and `ConsumerAuthEndpointFilter` accepting an
  `?api_key=` query-parameter fallback for the Realtime route (Phase 4 — browser `WebSocket` genuinely cannot
  set custom headers, so there was no frontend-only way to authenticate that one connection). No Domain/
  Application changes either way.

## Phase 1 — Frontend scaffold: project, shell, routing, API client

- [x] `src/AiProxy.Web/`: Vite + React + TypeScript project (`npm create vite@latest`), FluentUI v9 installed
  (`@fluentui/react-components`, `@fluentui/react-icons`), `react-router-dom`.
- [x] App shell (`layout/AppShell.tsx`): `FluentProvider` (light theme), left `NavDrawer` sidebar with the
  top-level sections — Config (Model Deployments, Consumers), Playground (Chat, Images, Voice), Dashboard
  (Usage), Settings — driving a `react-router-dom` `<Routes>` tree (single source of truth in `routes/routes.tsx`,
  shared by both the sidebar and the router). Note: `NavItem`s must be direct children of `NavDrawerBody` —
  `NavDrawer` already provides the `Nav` context via `onNavItemSelect`/`selectedValue` on itself, and wrapping
  them in a nested `<Nav>` (an early draft did this) creates a second, disconnected context that silently
  swallows every click. Caught via the app-shell navigation test, not assumed.
- [x] `apiClient` module (`api/client.ts`): `adminFetch`/`consumerFetch` (attach `X-Admin-Api-Key`/`X-Api-Key`
  respectively) + `adminJson`/`consumerJson` helpers, parsing non-2xx JSON error bodies into a thrown
  `ApiError { status, message, headers }`. Same-origin (no base URL), with `vite.config.ts`'s dev-server proxy
  making that true in `npm run dev` too.
- [x] Settings page (`pages/SettingsPage.tsx`): Admin API Key + Consumer API Key inputs backed by
  `api/settingsStore.ts` (`sessionStorage`); a connection-status badge pinging unauthenticated `GET /health/ready`.
- [x] Placeholder pages for every route (`pages/config/*`, `pages/playground/*`, `pages/dashboard/*`) wired
  into the sidebar.
- [x] `vitest` + React Testing Library set up (`vite.config.ts`'s `test` block + `src/test/setup.ts`); smoke
  tests in `src/test/App.test.tsx` (app shell renders, every sidebar link navigates to its page) and
  `src/test/client.test.ts` (header attachment + error-body surfacing) — 5/5 passing.

## Phase 2 — Single-container serving pattern

- [x] `src/AiProxy.Web/vite.config.ts`: `build.outDir` → `../AiProxy.Api/wwwroot`, `build.emptyOutDir: true`;
  dev-server `proxy` for `/api`, `/openai` (`ws: true` — the Realtime route is a WebSocket upgrade),
  `/anthropic`, `/health` → the API's `launchSettings.json` dev port.
- [x] `.gitignore`: added `src/AiProxy.Api/wwwroot/*` + `!src/AiProxy.Api/wwwroot/.gitkeep` (same pattern as
  `logs/`/`test-results/`); `.gitkeep` placeholder added. (`src/AiProxy.Web/node_modules`/`dist` were already
  covered by the Vite-scaffolded `src/AiProxy.Web/.gitignore`.)
- [x] `AiProxy.Api/Program.cs`: `UseDefaultFiles()`/`UseStaticFiles()` added to the pipeline; the old
  placeholder `MapGet("/", ...)` removed (the frontend now owns `/`); `MapFallbackToFile("index.html")`
  registered last, falling back to a plain-text `MapFallback` hint when `wwwroot/index.html` doesn't exist yet.
  New `FrontendFallbackTests.cs` (3 tests) confirms `/` and an arbitrary client-side route both 200, **and**
  that the fallback does not shadow `/health/live`, unauthenticated `/openai/v1/chat/completions` (still 401),
  or unauthenticated `/api/v1/config/model-deployments` (still 401) — verified, not assumed.
- [x] `Dockerfile`: added a `frontend-build` (`node:22-alpine`) stage running `npm ci && npm run build`.
  **The "publish picks wwwroot up automatically" assumption did not hold** — each Docker stage is an isolated
  filesystem, so the `build` stage needs an explicit `COPY --from=frontend-build /src/AiProxy.Api/wwwroot
  src/AiProxy.Api/wwwroot` before `dotnet publish` (added). `.dockerignore` updated to exclude
  `src/AiProxy.Web/node_modules/` and `src/AiProxy.Api/wwwroot/` from the build context. Verified end-to-end:
  full `docker build`, then `docker run` + `curl /` returned the built `index.html` (200); image/container
  cleaned up afterward.
- [x] `docker-compose.yml`: confirmed no changes needed (same single `api` image/service/build context).
- [x] `AiProxy.Api.csproj`: confirmed `wwwroot` isn't excluded — proven by the Docker smoke test above (no
  explicit `<Content Include="wwwroot/**" />` needed; default SDK web-project globs already include it).
- [x] README.md: new "Frontend (`src/AiProxy.Web`)" section documenting both workflows (build-for-production
  vs. `npm run dev` hot reload against a locally-running API).

## Phase 3 — Config pages

- [x] **Model Deployments** (`pages/config/ModelDeploymentsPage.tsx`): list (name, strategy, endpoint health
  summary badge), create (name — strategy omitted from the form since `RoutingStrategy` has only one value
  today, hardcoded to `Priority`), delete (`ConfirmDialog`, cascades endpoints per §7.2, called out in the
  dialog copy). Edit-strategy skipped for the same single-value reason (see Decisions). Row click navigates to
  a detail page (`/config/model-deployments/:name`, not in the sidebar).
- [x] **Endpoints** (`pages/config/ModelDeploymentDetailPage.tsx`): list (base URL, API family, priority, auth
  mode, enabled toggle, health badge, timeout, max retries), add (all `AddEndpointInput` fields incl. the
  conditional API-key/managed-identity-scope input based on auth mode), inline enabled toggle
  (`PUT .../endpoints/{id}`), remove (`ConfirmDialog`), set/replace secret (separate dialog calling
  `PUT .../secret`, not bundled into the add/edit form — matches the API's write-only secret design). Priority
  edit is currently only settable at add time, not via a dedicated inline edit control (`UpdateEndpointInput`
  supports it; the UI doesn't expose it yet) — a small gap, noted for a follow-up pass rather than blocking
  the rest of Phase 3.
- [x] **Consumers** (`pages/config/ConsumersPage.tsx`, minimal, see Decisions): list (name, status badge,
  created), create (name only, `SecretRevealDialog` shows the plaintext key exactly once with a
  copy-to-clipboard button and an explicit "you will not see this again" warning), rotate (same dialog),
  revoke (`ConfirmDialog`).
- [x] Form validation: required-field/disable-until-valid checks client-side (e.g. Create disabled until a
  name is entered); the backend's FluentValidation remains the source of truth — errors surface verbatim via
  `ErrorBanner` (`ApiError.message`, plus any `Retry-After` header — Iteration 2 Phase 5), never swallowed.
- [x] Component tests: `ConsumersPage.test.tsx` (create + one-time-reveal, revoke-only-after-confirm) and
  `ModelDeploymentsPage.test.tsx` (delete-only-after-confirm) — 4 new tests, all passing. Found and fixed two
  real testing-infrastructure bugs along the way (now in repo memory): jsdom lacks `ResizeObserver`
  (`MessageBar` crashes without a stub in `src/test/setup.ts`), and `userEvent.type()` on a FluentUI `Input`
  inside a `Dialog` breaks subsequent `getByRole` queries in the same dialog — worked around with
  `fireEvent.change`.

## Phase 4 — Playground

- [x] **Deployment picker** (`components/DeploymentPicker.tsx`): shared `Combobox` across all three tabs —
  fetches `GET /api/v1/config/model-deployments` (admin key) and lets the operator pick which deployment to
  send test traffic to.
- [x] **Chat tab** (`ChatPlaygroundPage.tsx`): message list + input, streaming toggle. Streaming reads the SSE
  body via `api/sse.ts`'s `readSseEvents` (Fetch API `ReadableStream`, no `EventSource` since that can't POST/
  set headers) and renders `delta.content` tokens incrementally; non-streaming awaits the full JSON response.
  Token usage (prompt/completion/total) shown once available in either mode — the backend forces
  `stream_options.include_usage` on the outbound streaming request regardless (architecture.md §10), so the
  final SSE chunk always carries it.
- [x] **Images tab** (`ImagePlaygroundPage.tsx`): prompt form posting to `/openai/v1/images/generations`,
  rendering returned images from either `url` or `b64_json` (whichever the configured backend returns).
- [x] **Voice tab** (`VoicePlaygroundPage.tsx`): connect/disconnect (`new WebSocket(...)` against
  `/openai/v1/realtime?model=...`), a JSON textarea pre-filled with a working `conversation.item.create`
  example + a "Send event" button, and a scrolling raw-event log. **The anticipated auth constraint was
  real**: browser `WebSocket` cannot set custom headers, so `ConsumerAuthEndpointFilter` (Infrastructure) was
  extended to accept the Consumer key via an `?api_key=` query parameter as a fallback when `X-Api-Key` is
  absent — header still takes precedence everywhere else. This is the one Iteration 3 change outside
  `src/AiProxy.Web` (Decisions said "frontend-only... [plus SPA fallback]"; this is the second necessary
  exception, called out here rather than silently expanding scope). New backend test
  (`RealtimeProxyTests.SuccessfulHandshake_AuthenticatesViaApiKeyQueryParam_WhenNoHeaderIsSet`) covers it;
  270/270 backend tests passing.
- [x] Error states across all three tabs surface the backend's actual error body via the shared `ErrorBanner`
  (status + message, plus `Retry-After` when present — Iteration 2 Phase 5).
- [x] Component test: `ChatPlaygroundPage.test.tsx` (non-streaming send → reply + token usage rendered).
- [x] Two real frontend-testing bugs found/fixed while writing these tests, both now in repo memory: the
  `ResizeObserver` stub (Phase 3) needed no changes, but the FluentUI-Dialog-vs-`userEvent` issue recurred in
  a new, load-dependent form — `user.click()` right after a `fireEvent.change` inside a Dialog intermittently
  breaks a later `getByRole` query, reproducing only under full-suite load, not in isolation. Fixed by using
  `fireEvent.click` instead of `user.click` for that specific sequence; verified stable across 3 repeated
  full-suite runs.

## Phase 5 — Usage dashboard

- [x] Filter bar (`UsageDashboardPage.tsx`): model deployment, consumer (free-text — both filter
  server-side by exact/partial name via the existing query params), operation type (`Combobox` over all
  `OperationType` values + "Any"), date range (plain HTML `<input type="date">`, not a FluentUI DatePicker
  package — avoids a new dependency for two simple date fields), pagination (Previous/Next, fixed page size
  of 25) — all mapping directly onto `GET /api/v1/usage`'s existing query parameters (`api/usage.ts`).
- [x] Table view: `usage_records` rows (timestamp, deployment, consumer, operation type, total tokens,
  streaming, status code, duration).
- [x] Chart views (`recharts`): tokens-by-deployment bar chart and requests-per-day line chart, computed
  client-side from the currently-loaded page only — flagged in the UI itself ("Charts reflect only the
  currently loaded page...") since there's no backend aggregation endpoint in this iteration.
- [x] Empty ("No usage records match these filters"), loading (`Spinner`), and error (`ErrorBanner`) states.
- [x] Component tests (`UsageDashboardPage.test.tsx`): records render in the table, and the empty state shows
  when the query returns nothing.
- [x] `vite.config.ts`: set `test.fileParallelism: false` — the FluentUI-Dialog/Tabster test flakiness from
  Phase 3/4 (repo memory) turned out to be genuinely load-dependent (worse with more test files running
  concurrently, not fully fixed by `fireEvent.click` alone); running test files sequentially removed the
  contention entirely, verified stable across 3+ repeated full `npm test` runs. Slower suite, but deterministic.

## Phase 6 — Documentation

- [x] `architecture.md`: new §19 "Frontend (Iteration 3)" (stack, single-container serving pattern, auth
  stand-in, Realtime Playground scope + the query-param auth exception, Usage dashboard's client-side-only
  chart limitation); updated §2 scope table (6.3 cross-ref, non-goals), §4 (solution tree + layer
  responsibilities + new **Web** bullet), §12 (new frontend fallback row), §13 (query-param auth fallback),
  §14 (Dockerfile frontend-build stage), §16 (Vite/React/FluentUI/recharts/Vitest rows), §17 (auth stand-in
  assumption), §18 (Admin UI non-goal updated to reflect what Iteration 3 actually delivered vs. still missing).
- [x] Diagram 01 (high-level architecture): added a "Browser (Admin/Ops Frontend)" client box and updated the
  ingress box's label to note it also serves the React SPA/static files — re-exported to PNG.
- [x] `README.md`: added a "Pages" bullet listing what's actually implemented (Config/Playground/Dashboard
  pages) to the existing Frontend section (dev workflow was already covered when Phase 2 landed).
