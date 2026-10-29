# Project history

> Moved from the README. Newest revision first within the revision history. Dated verification notes come first.

## Verification notes

### 2026-10-27 — Fix pricing API compile error

- **Fixed:** renamed the invalid-order promotion local in `PricingService.Calculate` to `invalidOrderPromotion`. This avoids C# compiler error CS0136 because the later successful-calculation path also declares a local named `promotion`.
- **Verification status:** source-level fix applied; rerun `docker compose up --build` to verify the API image builds and the stack starts. The build output also reports a NU1903 security advisory for transitive package `Microsoft.OpenApi` 2.0.0; it is a warning, separate from the compile failure, and should be addressed in a dependency update.

### 2026-10-27 — Fix Worker hosting dependency

- Added an explicit `Microsoft.Extensions.Hosting` 10.0.0 package reference to the Worker project. The Worker uses `Host.CreateApplicationBuilder`, `BackgroundService`, and `ILogger<T>`; these hosting APIs were not referenced by the project, causing Docker compilation to fail.
- Local Docker rebuild and runtime verification still need to be run by the developer; the patch itself has not been compiled in this environment.

### 2026-10-28 — Local Docker deployment milestone, SDK alignment, and editor conventions

- **Reported local verification:** the developer reports that `docker compose up --build` completed far enough for the services to start and `GET /health` to return `Healthy`. This is recorded as a user-reported smoke check, not as a test run independently reproduced in this environment. Continue checking container status/logs and run automated tests.
- **SDK diagnosis:** the developer's host has .NET SDK `8.0.204`, while the API, Worker, and tests target `net10.0`. Docker uses .NET 10 images, so container builds can succeed while VS Code's host-side NuGet restore fails with `NETSDK1045`. Documented installing the .NET 10 SDK and added `global.json` to select the .NET 10 SDK family.
- **Formatting conventions:** added `.editorconfig` for consistent spaces, per-language indentation, final newlines, and trailing-whitespace handling. Added VS Code workspace settings for spaces and format-on-save where language formatters support it. Frontend Prettier/ESLint enforcement remains a follow-up; the README recommends tools but does not claim they are installed or run by CI.
- **Dependency advisory:** pinned the transitive `Microsoft.OpenApi` dependency to patched 2.x version `2.7.5` to address the reported `NU1903` advisory for version `2.0.0`. Verify restore/build and review the resolved dependency graph after installing .NET 10; do not assume the warning is resolved until NuGet audit output confirms it.
- **Still unverified:** full backend test suite, frontend formatting/lint scripts, and CI results have not yet been confirmed.

## Revision history

### 2026-10-28 — Infrastructure trimmed, rule confirmed, dependency and AI-usage notes
- **RabbitMQ and PostgreSQL removed** from `compose.yaml`, `compose.selfhost.yaml`, `.env.example` and the CI smoke test, with the settings, volumes and `depends_on` entries that went with them. No code used either. If you ran the old stack, use `docker compose up --build --remove-orphans` once.
- **Usage audit:** the placeholder `worker` (project, Dockerfile, two compose services, a CI image build and its solution entry) and 26 unused CSS rules (25 prototype leftovers in `style.css`, one in `components.css`) were removed. Everything else was checked and is used; see `docs/USAGE-AUDIT.md`.
- **Confirmed:** a customer can cancel only while the order is Not Started. Recorded in `DESIGN.md`, `LIFECYCLE.md` and ADR-0007.
- **Deferred as out of scope for this session:** changing a placed order (`docs/FUTURE-WORK.md`, which now opens with a one-page backlog of every deferred item).
- **New:** `docs/DEPENDENCY-NOTES.md` (what `npm ci` reports: deprecations, 4 dev-tooling vulnerabilities with 0 in production, and the esbuild install-scripts warning) and `docs/AI-USAGE-GUIDE.md`.
- **Fixed:** the `xUnit2031` analyzer warning in `OrderIdempotencyTests`.
- **README rewritten to be shorter.**

### 2026-10-28 — Fixes from the first real build (Windows, stock xunit and its analyzers)
- **Compile error CS0433 (4 errors):** the order service's `Program.cs` used top-level statements, which generate a type named `Program`; the builder API already has one, and the integration-test project references both, so the type name was ambiguous. The service's entry point is now an explicit `OrderServiceProgram.Main`. This could not be seen earlier because the stand-in test runner compiled everything into one assembly; the check harness now builds the tests as a separate assembly referencing both back ends, which reproduces the error exactly.
- **41 xunit analyzer warnings:** `xUnit2013` (`Assert.Equal(0|1, collection.Count)` is now `Assert.Empty` / `Assert.Single`), `xUnit2017` (`Assert.True(set.Contains(x))` is now `Assert.Contains`), and `xUnit1031` (the race tests blocked on tasks with `Wait`, `WaitAll` and `.Result`; they are now `async` and `await`).
- Still true: the stock xunit runner has not been run by the author of these changes. Please run `dotnet test LongPizzaOrderBuilder.slnx`.

### 2026-10-28 — Separate order service, restaurant side, cancel, and the README rewrite
- **Order service:** a new process (`LongPizzaOrderBuilder.OrderService`, port 8090) owns orders and their lifecycle. The builder API (port 8080) now offers only the menu and a price preview. Pricing moved to a shared library, `LongPizzaOrderBuilder.Pricing`, used by both (ADR-0009).
- **Statuses:** Not Started, Started, Ready for Pickup, Delivered, plus Cancelled. A customer can cancel only while Not Started; the restaurant moves one step forward and cannot cancel. `Idempotency-Key` is now required on create.
- **Changing a placed order was prototyped and then deferred.** It needs a design discussion and a business discussion to justify it; the questions, the starting design and the withdrawn invariants (ORD-005, ORD-006, UI-018) are in `docs/FUTURE-WORK.md`. A customer can cancel and place a new order.
- **Restaurant side:** `/api/restaurant/orders` (list, transition), `scripts/restaurant.sh`, and an optional timer. New scripts: `dev.sh`, `seed.sh`, `demo-order-service.sh` (replaces `demo-lifecycle.sh`).
- **Invariants:** registry rewritten for the service (46 rules, 45 enforced). Race tests freeze one request mid-change; removing the lock from `Transition` or `Create` fails them.
- **Docs:** the README is now two parts (run, API and tests; then the design in one page). The old README was split into `PLANNING.md`, `OPERATIONS.md` and this file. `LIFECYCLE.md` and `DESIGN.md` rewritten for the service; ADR-0009, ADR-0010 and `FUTURE-WORK.md` added.
- **Verified where authored:** backend 77 tests (unit, and both back ends over real HTTP) through stand-ins for xunit and `WebApplicationFactory`; frontend 77 tests, lint, typecheck and build; the three processes driven through a browser. **Not run:** the stock xunit runner, Docker Compose, CI.

### 2026-10-28 — Order lifecycle, safe retries and race handling
- **Lifecycle:** `OrderStatus` (Submitted, InProgress, Completed, Cancelled) with one transition table, recorded history, and a customer cancel that is allowed only before the kitchen starts. New endpoints `POST /api/orders/{id}/cancel` and `POST /api/orders/{id}/transitions`.
- **Concurrency:** all order changes run under one lock with immutable snapshots, so a cancel and a kitchen start at the same moment have exactly one winner and the loser is told the truth (HTTP 409 plus the current order).
- **Retries:** `Idempotency-Key` on order creation (201 new, 200 repeat, 422 conflicting reuse). The UI makes one key per checkout, times out after 10 seconds into an honest "may or may not have been received" message, and reuses the key on retry.
- **UI:** a status panel (words plus a symbol, history, cancel button only when the server allows it) that polls until the order is final and never moves backwards.
- **Verified where it was authored:** backend 69 tests (unit and the real app over HTTP, through stand-ins for xunit and `WebApplicationFactory`), frontend 77 tests plus lint, typecheck and build; mutation checks for every lifecycle rule, including removing the lock. Not run: real xunit, Compose, CI. See `docs/LIFECYCLE.md`.

### 2026-10-28 — Multi-pizza cart, invariant registry, decision records
- **Frontend:** the UI now builds several pizzas. Edits apply to the selected pizza so the running total moves with every change.
  All amounts shown come from the server's `breakdown`; the browser does no price arithmetic. Added a pending state, debounced
  and cancellable previews, visible validation errors, an accessible order-details dialog, keyboard-operable pickers, ESLint and Vitest.
- **Backend:** `PricingService` takes the menu by injection and reads promotion rules (including the SAVE5 threshold) from menu
  data instead of hardcoding them. `GET /api/menu` gains an additive `minimumSubtotalExclusive` field on promotions.
- **Tests:** added invariant tests checked against an independent integer-cents oracle (about 20,000 orders), and replaced a test that
  only exercised `Math.Round` with one that exercises the service. `docs/INVARIANTS.md` plus `scripts/check-invariants.mjs` tie rules to tests in CI.
- **Verified where it was authored:** frontend typecheck, lint, 46 tests and production build; the unit-test files ran against the real
  service code through a minimal xunit stand-in (28 passed); the real API and new UI were driven together in Chromium. **Not run there:**
  the real xunit runner and `WebApplicationFactory` integration tests (NuGet was unreachable), Docker Compose, and GitHub Actions.
  Run `scripts/verify.sh` locally and confirm CI before relying on this.
- Fixed `scripts/verify.*`, which pointed at a test project that no longer exists.

### 2026-10-26 — Initial project and domain design

- **Implemented in source:** Docker-oriented React + TypeScript and ASP.NET Core starter; menu and order endpoints; basic pizza customization UI; in-memory order storage; Compose configuration for PostgreSQL and RabbitMQ; health endpoint and CI workflow scaffold.
- **Designed / agreed:** pizza sizes Small $5, Medium $7, Large $12; Basic toppings at 8% of base price; Premium toppings at 11%; 6.5% tax; `ILOVEPIZZA` 10% off before tax; `SAVE5` subtracts $5 only when the pre-discount subtotal is greater than $20; promotions cannot be combined; tips can be percentages or custom amounts.
- **Architecture decision:** keep a modular monolith first, use REST for interactive operations, and use RabbitMQ events for asynchronous downstream processing when wired in.
- **Alternatives weighed:** enums/static catalogs versus data-driven menu options; hardcoded promotion switches versus explicit promotion rule records; in-memory orders versus PostgreSQL; direct event publishing versus a transactional outbox; duplicated frontend calculations versus a shared server pricing engine. Trade-offs are documented above.
- **Known gaps at that revision:** the prototype prices did not match the agreed rules; preview pricing, PostgreSQL persistence, RabbitMQ publishing/consuming, and complete automated tests were not yet integrated. The later 2026-10-27 revision below records the pricing API milestone.

### 2026-10-27 — Order builder, pricing UX, and lifecycle requirements

- **Requirements documented:** orders contain one or more configured line items with quantities; item changes recalculate all dependent totals; empty drafts cannot be submitted; promotion failures explain unmet conditions; tips recalculate according to a defined tip base.
- **UX decisions:** detailed order description is shown only on explicit request in an accessible dialog; accepted orders show an ID, receipt, lifecycle status, and ETA only when available; delayed/cancelled states provide customer-safe reasons when known.
- **Lifecycle design:** documented Draft, Submitted, InProgress, Delayed, Completed, and Cancelled states, server-side transition validation, status history, idempotent submission, and reliable lifecycle events.
- **Quality gates:** added critical invariants and a prioritized automated test matrix for pricing, promotion boundaries, API validation, frontend state races, accessible dialog behavior, order idempotency, lifecycle transitions, persistence, outbox, and end-to-end customer journeys.
- **Alternatives weighed:** modal versus always-visible order details; automatic versus customer-selected promo application; polling versus push status updates; local UI arithmetic versus server-authoritative preview; merging identical line items versus keeping them separate. The README records the selected direction and open policy questions.
- **Implementation status at that revision:** these were requirements and test plans, not evidence that the lifecycle, pricing preview, persistence, messaging, or tests had been implemented. The later revision below records the pricing API and test-source work; persistence and messaging remain outstanding.

### 2026-10-27 — Pricing APIs, integration tests, and self-host deployment scaffold

- **Implemented in source:** agreed menu pricing and topping rates; server-side pricing preview; tax, tips, `ILOVEPIZZA`, `SAVE5`, validation, and rounding; order creation with server-side recalculation; order listing and retrieval by ID; health endpoint.
- **Tests added in source:** pricing unit tests plus HTTP-level API integration tests for menu, preview, invalid selections, order submission, and retrieval. These tests have not been executed in the current environment.
- **Deployment:** added the worker to the development Compose stack, pinned direct frontend dependency versions, and expanded CI to test the test project and build all three container images. The self-host Compose profile keeps database and broker ports private and serves the frontend through Nginx.
- **Important remaining gaps:** order storage is still in-memory; PostgreSQL/EF Core persistence, migrations, RabbitMQ event publishing/consuming, and idempotent submission are not wired. The worker is currently a heartbeat scaffold.
- **Verification status:** source JSON/YAML syntax checks passed. At that authoring revision, the environment did not have the .NET SDK or Docker CLI, and package registry DNS access failed during npm installation. Frontend/backend/container builds and tests could not be executed here. A `package-lock.json` could not be generated in this environment; run `npm install` successfully and include the generated `src/frontend/package-lock.json` in the first check-in. `scripts/verify.ps1`, `scripts/verify.sh`, and CI checks provide repeatable verification in an environment with those prerequisites.

### 2026-10-28 — Prepare for first Git upload

- Expanded `.gitignore` to exclude local environment files/secrets, .NET and frontend build output, test results, coverage, IDE state, logs, and OS metadata while preserving `.env.example` and shared VS Code settings.
- Added `.gitattributes` to normalize text line endings across Windows, macOS, and Linux.
- Added a first-upload checklist describing which source/configuration/docs/tests/CI files belong in Git, which generated artifacts and local secrets must stay out, how to review the staging area, and how to make and push the initial commit.
- Corrected the local restore instructions to use the test project directly; this repository does not currently contain `LongPizzaOrderBuilder.sln`.
- **Still required before first upload:** run a successful `npm install` to create `src/frontend/package-lock.json`, review the staged diff for secrets/artifacts, and verify the actual CI result after pushing.

### 2026-10-28 — Test-project split and local verification

- Split automated backend tests into `tests/LongPizzaOrderBuilder.UnitTests` and `tests/LongPizzaOrderBuilder.IntegrationTests`, both targeting .NET 10 and referencing the API project.
- Added/organized `LongPizzaOrderBuilder.slnx` to include the API, Worker, and both test projects; corrected namespace/project discovery issues in VS Code.
- **Locally verified:** `dotnet test .\LongPizzaOrderBuilder.slnx` passed 17 tests with 0 failures and 0 skipped tests. Integration tests exercised menu, pricing preview, invalid requests, order creation, and retrieval using `WebApplicationFactory`.
- **Reported local Compose check:** the stack started and `/health` returned `Healthy`; follow up with `/api/menu`, container health/log review, and a repeatable smoke check.
- **Not yet verified:** Release-mode solution build/test, frontend build in the current environment, and a green GitHub Actions run. Ensure `.github/workflows/ci.yml` references the `.slnx` and split test projects; do not treat CI/CD as verified until the workflow passes.

### Next revision — planned, not yet implemented

- Run `scripts/verify.ps1` (Windows) or `scripts/verify.sh` (macOS/Linux), and confirm the GitHub Actions workflow passes; record the actual results.
- Replace in-memory order storage with PostgreSQL/EF Core migrations and durable order/receipt snapshots.
- Implement submission idempotency and a transactional outbox; connect RabbitMQ and replace the worker heartbeat with real consumers.
- Add a browser end-to-end test for a full build-preview-submit-retrieve journey.
- Revisit deployment security, TLS, backup/restore, and CI registry publishing before internet-facing use.
