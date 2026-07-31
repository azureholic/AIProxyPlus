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
  architecture.md). Iteration 3 is frontend-only work plus the minimal Program.cs static-file/SPA-fallback
  wiring — no Domain/Application/Infrastructure changes.

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

- [ ] **Model Deployments**: list (name, strategy, endpoint count/health summary), create (name + routing
  strategy), edit (strategy), delete (with a confirm dialog — cascades endpoints per §7.2, worth calling out
  in the UI).
- [ ] **Endpoints** (nested under a deployment's detail page): list (base URL, API family, priority, auth
  mode, enabled, timeout, max retries), add (all `AddEndpointBody` fields, incl. the API key input for
  `AuthMode.ApiKey` which is write-only — never redisplayed after save), edit (priority/enabled/timeout/max
  retries — not base URL/API family/auth mode, matching `UpdateEndpointBody`'s actual fields), remove (confirm
  dialog), rotate/replace secret (separate action calling `PUT .../secret`, not bundled into the general edit
  form, mirroring the API's own write-only secret design).
- [ ] **Consumers** (minimal, see Decisions): list (name, enabled, created/revoked — never key/hash), create
  (name only; show the plaintext API key returned **exactly once** in a copy-to-clipboard dialog with an
  explicit "you will not see this again" warning), rotate (same one-time-reveal dialog), revoke (confirm
  dialog).
- [ ] Form validation mirrors the backend's FluentValidation rules closely enough to avoid round-tripping
  obviously-invalid input (e.g. required fields, priority ≥ 0), but the backend remains the source of truth —
  server validation errors are surfaced verbatim, not swallowed.
- [ ] Component tests: at minimum, the create-consumer one-time-reveal flow and the delete-confirmation flows
  (the two places a mistake is hardest to recover from).

## Phase 4 — Playground

- [ ] **Deployment picker**: shared component across all three playground tabs — fetches
  `GET /api/v1/config/model-deployments` (admin key) and lets the operator pick which deployment to send
  test traffic to.
- [ ] **Chat tab**: a minimal chat UI (message list + input) posting to `/openai/v1/chat/completions` with the
  Consumer key; a streaming toggle — when on, reads the SSE body via the Fetch API's `ReadableStream` and
  renders tokens incrementally; when off, awaits the full JSON response. Shows the final `usage` figures
  (prompt/completion/total tokens) once available for either mode.
- [ ] **Images tab**: a form (prompt, size/quality if applicable) posting to
  `/openai/v1/images/generations`, rendering the returned image(s) (URL or base64, whichever the configured
  backend returns).
- [ ] **Voice tab**: per the reduced scope in Decisions — connect/disconnect button
  (`new WebSocket(...)` against `/openai/v1/realtime?model=...`, same-origin so the browser handles the
  `X-Api-Key`... **note to verify during implementation**: browser `WebSocket` cannot set custom headers, so
  the Consumer key likely needs to travel as a query parameter for this one route specifically — this is a
  real constraint to resolve in Phase 4, not Phase 1's assumption of header-based auth everywhere. A text
  input + "send event" button (raw JSON textarea, pre-filled with a working `conversation.item.create` +
  `response.create` example) and a scrolling raw-event log of everything received.
- [ ] Error states across all three tabs surface the backend's actual error body (status + message/headers
  where relevant, e.g. a 429's `Retry-After` — see Iteration 2 Phase 5) rather than a generic "something went
  wrong".

## Phase 5 — Usage dashboard

- [ ] Filter bar: model deployment, consumer, operation type, date range — mapping directly onto
  `GET /api/v1/usage`'s existing query parameters, with pagination controls matching its `page`/`pageSize`.
- [ ] Table view: raw `usage_records` rows (timestamp, deployment, consumer, endpoint, operation type,
  tokens/usage metrics, status code, duration, streaming).
- [ ] Chart views (`recharts`): tokens-by-deployment bar chart and requests-over-time line chart, computed
  client-side from the currently-filtered page(s) — no new backend aggregation endpoint in this iteration
  (flagged as a known limitation if the result set is large/paginated, since the chart only reflects the
  loaded page, not the full filtered set).
- [ ] Empty/loading/error states consistent with the rest of the app.

## Phase 6 — Documentation

- [ ] `architecture.md`: new §19 "Frontend (Iteration 3)" — stack choice, single-container serving pattern,
  the Settings-page auth stand-in, the reduced Realtime Playground scope; update §2 scope table, §4 solution
  structure tree (new `src/AiProxy.Web/`), §12 API surface summary (note the frontend consumes the existing
  surface, no new routes besides the SPA fallback), §14 deployment topology (frontend build stage in
  Dockerfile), §16 technology choices (Vite/React/FluentUI/recharts), §18 non-goals (full duplex voice
  playground, real auth).
- [ ] New/updated diagram: extend diagram 01 (high-level architecture) or add a new one showing the browser →
  static files + `/api`/`/openai`/`/anthropic` split within the same container.
- [ ] `README.md`: frontend dev workflow (already partly covered in Phase 2), screenshots optional/skipped
  (text-only docs, consistent with the rest of the repo).
