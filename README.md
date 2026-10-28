# LongPizzaOrderBuilder

A Docker-first starter for a customizable pizza ordering application. The intended stack is React + TypeScript, ASP.NET Core REST APIs, PostgreSQL, RabbitMQ, a background worker, and GitHub Actions.

> **Project status:** the developer has reported that `docker compose up --build` now starts the services and `/health` returns `Healthy`. This confirms a useful local deployment milestone, but does not yet establish that automated tests pass, that every service is healthy over time, or that PostgreSQL/RabbitMQ are integrated into order processing. The local Windows machine still has only .NET SDK 8.0.204, which cannot build this repository’s `net10.0` projects outside Docker. This README separates **implemented in source**, **reported as verified locally**, and **planned work**.

## Contents

- [Project goals](#project-goals)
- [Current implementation status](#current-implementation-status)
- [Business rules and pricing decisions](#business-rules-and-pricing-decisions)
- [Implemented API endpoints](#implemented-api-endpoints)
- [Domain model direction](#domain-model-direction)
- [API contract direction](#api-contract-direction)
- [Architecture decisions and alternatives](#architecture-decisions-and-alternatives)
- [Local development and self-hosting](#local-development)
- [First Git repository upload](#first-git-repository-upload)
- [Roadmap](#roadmap)
- [Critical invariants and test plan](#critical-invariants)
- [Revision history](#revision-history)

## Project goals

LongPizzaOrderBuilder should let a customer configure one or more pizzas, understand the price as choices change, apply promotions, choose a tip, and submit an order. The same business rules should be enforced by the C# backend and presented clearly by the React UI.

Design principles:

1. **The server is authoritative for price.** The browser sends choices, not trusted totals or discounts.
2. **Menu options should be data-driven.** Adding a size or topping should generally be a menu/configuration change rather than a code change.
3. **Preview and checkout should share the same pricing engine.** A preview is an estimate; order submission recalculates and persists the authoritative result.
4. **Order receipts must be stable.** Store price, discount, tax, and total snapshots so future menu changes do not rewrite historical orders.
5. **Use REST for immediate request/response and events for asynchronous work.** Do not introduce distributed services without a concrete need.
6. **Keep local and cloud environments container-friendly.** Use Docker Compose locally and deploy the same built images to a chosen cloud container platform.

## Current implementation status

Status reflects the source in this revision; do not infer runtime verification from source inspection alone.

| Area | Current source status | Notes |
|---|---|---|
| React + TypeScript UI | Implemented | Uses the menu API and calls the server pricing preview as selections change. Build verification is pending in this environment. |
| `GET /api/menu` | Implemented | Returns agreed size prices, crust choices, topping rates, tax rate, and promotion descriptions. |
| `POST /api/pricing/preview` | Implemented | Validates customer selections and returns a server-calculated breakdown without creating an order. |
| `POST /api/orders` | Implemented prototype | Revalidates and reprices before accepting; order storage remains in-memory and is lost on API restart. |
| `GET /api/orders` and `GET /api/orders/{id}` | Implemented prototype | Lists and retrieves orders in the current API process only. |
| `GET /health` | Implemented | Used by container health checks. |
| Local Docker Compose smoke test | Reported successful by developer | Services appeared to start and `/health` returned `Healthy`; automated tests and longer-running health have not yet been confirmed. |
| Pricing unit tests | Implemented in source | Covers size prices, topping rates, promo threshold, tax, tips, validation, and rounding; execution is pending. |
| API integration tests | Implemented in source | Exercises menu, preview, validation, order creation, and retrieval through an in-memory test server; execution is pending. |
| PostgreSQL | Provisioned in Compose | EF Core persistence and migrations are not wired into the order workflow. |
| RabbitMQ and worker | Provisioned in Compose | Worker currently logs a heartbeat; RabbitMQ publishing/consuming and order events are not implemented. |
| Self-hosted Compose | Implemented scaffold | Production frontend is served by Nginx, which proxies `/api` to the API. The database and broker are not published on host ports in this profile. |
| CI | Implemented workflow | Builds API/frontend/container images and runs the test project on GitHub Actions; not yet verified by a CI run. |

**Important:** this is a development milestone, not a production-ready ordering system. The API currently stores orders only in memory. Do not use it for real orders until durable persistence, migrations, backups, authorization, operational security, and reliable messaging are implemented and verified.

## Business rules and pricing decisions

These are the currently agreed initial product rules and are implemented by the current pricing service. The percentage-tip base remains a working assumption to confirm before production.

### Pizza sizes and base prices

| Size | Base price |
|---|---:|
| Small (`Small`) | $5.00 |
| Medium (`Medium`) | $7.00 |
| Large (`Large`) | $12.00 |

### Toppings

| Category | Toppings | Price rule |
|---|---|---|
| Basic | Pepperoni, sausage, extra cheese, onions, black olives, green peppers | 8% of the selected pizza's base price per topping |
| Premium | Bacon, chicken, garlic, artichoke hearts, sun-dried tomatoes, pineapple | 11% of the selected pizza's base price per topping |

For the initial rules, a topping rate is calculated against the pizza base price, not against the growing subtotal. Unless the product requirements change, each listed topping can be selected once per pizza; supporting extra portions should use an explicit selection quantity.

### Tax, tip, and promotion rules

- **Tax:** 6.5% of the order subtotal after promotion discounts and before tip.
- **Tip:** customer may choose no tip, a percentage tip, or a custom dollar amount. The current working assumption is that percentage tips apply to the post-discount, post-tax total. Confirm this before production because tip bases can be a business/legal policy decision.
- **`ILOVEPIZZA`:** 10% off the order subtotal before tax.
- **`SAVE5`:** $5 off when the eligible subtotal before discount and tax is strictly greater than $20. An order subtotal of exactly $20 does not qualify.
- **Promotion stacking:** `ILOVEPIZZA` and `SAVE5` cannot be combined. The initial design treats each as mutually exclusive.
- **Rounding:** monetary amounts use C# `decimal`, not binary floating-point. Round monetary line/subtotal/tax/discount values consistently to two decimal places using an explicitly tested policy. The earlier example uses `MidpointRounding.AwayFromZero`; final policy should be confirmed with the business and accounting expectations.

### Proposed calculation order

1. Calculate each pizza base price by selected size and quantity.
2. Calculate each topping surcharge as its rate multiplied by that pizza's base price and quantity.
3. Add pizza and topping amounts to form the pre-discount subtotal.
4. Validate and apply one eligible promotion.
5. Calculate tax at 6.5% on the discounted subtotal.
6. Calculate tip according to the selected tip policy.
7. Add discounted subtotal, tax, and tip for the final total.

The pricing engine must be the single source of truth for both preview and final order submission. The API must validate all product, size, and topping IDs against the current menu and must never accept client-supplied prices as authoritative.

## Implemented API endpoints

| Method | Path | Behavior |
|---|---|---|
| `GET` | `/api/menu` | Returns sizes, crusts, toppings, tax rate, and promotion descriptions. |
| `POST` | `/api/pricing/preview` | Accepts `{ items, promoCode, tip }`; returns `breakdown`, `promotion`, `errors`, and `isValid`. Does not create an order. Invalid item/tip selections return HTTP 400 with the response errors. |
| `POST` | `/api/orders` | Accepts customer name and either the legacy single-pizza fields or `items`; recalculates on the server and returns HTTP 201 with the order and a `Location` header. |
| `GET` | `/api/orders` | Lists orders held by the current API process. |
| `GET` | `/api/orders/{id}` | Returns one in-memory order or HTTP 404. |
| `GET` | `/health` | Container health check. |

Example preview request:

```json
{
  "items": [
    { "size": "Small", "crust": "Classic", "toppings": ["pepperoni"], "quantity": 1 }
  ],
  "promoCode": "ILOVEPIZZA",
  "tip": { "type": "Percent", "percent": 15 }
}
```

The API treats submitted amounts as untrusted: clients send only menu choices, quantities, promo code, and tip selection. The API calculates all monetary fields. `SAVE5` is not applied at exactly $20; an unrecognized or ineligible promo is returned as not applied with a message rather than silently changing the order.

## Domain model direction

The first prototype used a `PizzaSize` enum, a static `PizzaCatalog`, topping records, an order with item selections, a `PromoCode` enum, a `TipSelection`, and an `OrderPricingBreakdown`. These are useful for a demo, but a static catalog and promotion enum make frequent menu and promotion changes require code edits and redeployment.

The preferred direction is to separate menu data, customer configuration, pricing rules, and accepted orders:

- **`MenuProduct`** — a menu offering, with name, description, availability, and customization groups.
- **`PizzaSizeOption`** — a data-driven size with its ID, display name, base price, and allowed options.
- **`CustomizationOption`** — a topping, crust, sauce, cheese option, or other modifier, with availability and an explicit pricing strategy.
- **`PizzaConfiguration`** — the selected product, size, crust, customizations, and quantity for one configured pizza.
- **`SelectedCustomization`** — an option ID plus selected quantity, allowing extra portions later.
- **`PizzaOrder` / `PizzaOrderItem`** — submitted order and line items, including snapshots of selected labels and accepted prices.
- **`Promotion`** — a code, rule type, value, eligibility conditions, active dates/status, and stacking policy.
- **`TipSelection`** — none, percentage, or custom amount.
- **`OrderPricingBreakdown`** — subtotal, discount, taxable subtotal, tax, tip, total, and applied-promotion details/messages.
- **`OrderPricingService`** — validates selections and calculates preview and final pricing using the same rules.

Use `decimal` for money. Use enums for stable technical states when appropriate, but use database/configuration-backed records for options that product owners may add or change. Avoid accepting arbitrary price formulas from clients. If modifiers support more than one pricing behavior, model a constrained strategy such as fixed surcharge or percentage of base price and validate that exactly one strategy applies.

### Historical order snapshots

At order acceptance, persist enough information to reconstruct the receipt even if menu prices later change: selected option IDs and display names, unit/base price, modifier prices, subtotal, promotion/discount, taxable subtotal, tax, tip, currency, and final total. Accepted order snapshots should not silently change when the live catalog is edited.

## API contract direction

The menu, pricing preview, order creation, order listing/retrieval, and health contracts below exist in the current source. Promotion validation is currently part of the pricing-preview response; a separate promotion endpoint remains optional and is not implemented.

| Endpoint | Responsibility |
|---|---|
| `GET /api/menu` | Return available products, sizes, customization groups/options, prices, and relevant constraints. |
| `POST /api/pricing/preview` | Validate current selections and return an up-to-date pricing breakdown without creating an order. |
| `POST /api/promotions/validate` | Explain promotion eligibility and incompatibilities in customer-friendly terms. |
| `POST /api/orders` | Revalidate selections and promotions, recalculate price, persist the order and price snapshot, and return the accepted receipt. |
| `GET /api/orders/{id}` | Retrieve an accepted order and its receipt. |

Use request/response DTOs at the HTTP boundary rather than exposing EF entities or internal domain objects directly. The frontend should send IDs and choices; it should not send authoritative prices, tax, discounts, or totals.

### Frontend integration guidance

- Fetch the menu at startup and render options from API data rather than hardcoding size/topping lists in React.
- Keep the current configuration as client-side state, then call the preview endpoint when meaningful selections change.
- Debounce preview requests or cancel stale requests so an old response cannot overwrite a newer configuration's price.
- Show base price, topping surcharges, discount, tax, tip, savings, and total transparently.
- Display promotion rejection reasons and, where feasible, a useful message about what would make a promotion eligible.
- Treat preview prices as estimates; use the server's accepted order response as the checkout receipt.

## Architecture decisions and alternatives

| Decision | Why we chose it | Alternatives considered / trade-offs |
|---|---|---|
| React + TypeScript frontend | Strong component ecosystem and types for API contracts and UI state. | Plain JavaScript is less type-safe; server-rendered UI could be simpler but is not the chosen frontend requirement. |
| ASP.NET Core REST API in C# | Fits the required backend stack and provides a clear HTTP boundary. | GraphQL could help with complex client data needs, but REST endpoints are sufficient for this builder and easier to start with. |
| Modular monolith first | Keeps business rules cohesive and local development simpler while preserving module boundaries. | Immediate microservices add deployment, networking, data consistency, and operational costs before they are justified. A worker can be a separate process for asynchronous tasks. |
| Docker Compose for local services | Reproducible local setup for frontend, API, database, and broker; same images can be deployed to cloud container platforms. | Installing every dependency directly on a developer machine can be lighter for some workflows but causes more environment drift. Compose is not itself a production orchestrator. |
| PostgreSQL + EF Core as persistence target | Relational transactions and constraints suit orders, line items, and price snapshots; EF Core integrates naturally with .NET. | In-memory storage is convenient for a demo but loses data and cannot support reliable orders. Other databases remain possible, but are not needed for this initial relational domain. |
| RabbitMQ for asynchronous integration events | Separates order acceptance from downstream work such as kitchen notifications and order-status workflows. | Direct synchronous calls are simpler for one process but couple availability and response time. A cloud-native queue is also viable if hosting on a provider with a preferred messaging service. |
| Transactional outbox for reliable events | Prevents the database order commit and event publication from silently diverging after a crash. | Publishing directly after saving is simpler but can lose events between the two operations; a distributed transaction is usually more complex and less attractive here. |
| Data-driven menu and promotions | Allows adding options and simple promotions without rewriting core pricing code. | Enums/static dictionaries are very simple for a fixed demo, but become restrictive when menu values change frequently. Stable technical states can still use enums. |
| Shared server-side pricing engine | Keeps preview and checkout calculations consistent and prevents client tampering. | Duplicating formulas in React gives instant local estimates but risks drift. A client-side estimate may be added for responsiveness only if it is clearly non-authoritative and reconciled with the server. |
| Explicit promotion rules, not a generic rule language | Supports the current two promos with testable, understandable code. | A general-purpose promotion engine is more flexible but adds complexity; defer it until real requirements justify it. |
| Price snapshots on accepted orders | Protects receipt integrity when menu prices or promotions change. | Recalculating historical orders from the current catalog is simpler to store but produces incorrect historical totals. |

## Local development

### Prerequisites

- Docker Desktop or Docker Engine with the Docker Compose plugin.
- For backend development/tests outside containers: .NET 10 SDK (required; .NET SDK 8 cannot build these `net10.0` projects) and Node.js 22. Docker builds use .NET 10 images and are independent of the host SDK version.

From the repository root:

```bash
cp .env.example .env
# Windows PowerShell: Copy-Item .env.example .env
docker compose up --build
```

Development endpoints:

- React/Vite: <http://localhost:5173>
- API: <http://localhost:8080>
- Menu: <http://localhost:8080/api/menu>
- Pricing preview: `POST http://localhost:8080/api/pricing/preview`
- Orders: <http://localhost:8080/api/orders>
- API health: <http://localhost:8080/health>
- OpenAPI in development: <http://localhost:8080/openapi/v1.json>
- RabbitMQ management: <http://localhost:15672>
- PostgreSQL: `localhost:5432`

#### Install the matching .NET SDK on Windows

The projects target **.NET 10**. Your screenshots show host SDK `8.0.204`, which explains the `NETSDK1045` restore errors in the editor; it is not a Docker runtime failure. Install the .NET 10 SDK (the SDK includes the runtimes needed for local development) from the [official .NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), or use WinGet in PowerShell:

```powershell
winget install Microsoft.DotNet.SDK.10
```

Restart VS Code and open a new terminal, then verify that a 10.0 SDK is listed:

```powershell
dotnet --list-sdks
dotnet --version
```

The repository's `global.json` selects the .NET 10 SDK family once installed. SDK 8 may remain installed side-by-side; it does not need to be uninstalled. See Microsoft's [Windows installation guide](https://learn.microsoft.com/en-us/dotnet/core/install/windows) if installation or PATH detection fails.

Run backend tests from the repository root after installing the .NET 10 SDK:

```powershell
dotnet restore tests/LongPizzaOrderBuilder.Tests/LongPizzaOrderBuilder.Tests.csproj
dotnet test tests/LongPizzaOrderBuilder.Tests/LongPizzaOrderBuilder.Tests.csproj --configuration Release
```

Build the frontend outside Docker:

```bash
cd src/frontend
npm install --no-audit --no-fund
npm run build
```

### Editor formatting and linting conventions

The repository includes a root `.editorconfig` so supported editors agree on whitespace and indentation. Use spaces rather than hard tabs: **4 spaces for C#**, **2 spaces for TypeScript/TSX, JSON, YAML, and CSS**, and no trailing whitespace. Keep a final newline in text files. These are editor conventions; they do not automatically reformat every existing file.

For VS Code:

- Install the Microsoft **C# Dev Kit** extension and keep VS Code/the extension current for .NET 10 support.
- Enable **Editor: Format On Save** if desired. C# formatting is provided by the C# tooling; `.editorconfig` provides the shared rules.
- For the frontend, use **Prettier** to format TypeScript/TSX, JSON, CSS, and Markdown. The repository does not currently declare Prettier or ESLint as dependencies, so frontend linting is not yet enforced by an npm script or CI. Add and pin those tools, add `format`, `format:check`, and `lint` scripts, and run them in CI before treating linting as a quality gate.
- If VS Code does not pick up `.editorconfig`, run **Developer: Reload Window** and check that the relevant language formatter extension is enabled.

Suggested frontend tools: [Prettier](https://prettier.io/) for consistent formatting and [ESLint](https://eslint.org/) with the TypeScript and React hooks plugins for code-quality rules. Avoid adding auto-fix rules that silently alter behavior without reviewing the diff.

### Self-host with production-style containers

The `compose.selfhost.yaml` profile builds a static frontend image served by Nginx, an ASP.NET Core API image, a worker image, PostgreSQL, and RabbitMQ. Nginx exposes the app on one host port and proxies `/api` to the API over the private Compose network. PostgreSQL and RabbitMQ do not publish host ports in this profile.

1. Copy `.env.example` to `.env` and replace **every** example password with a long, unique secret. Keep `.env` out of source control.
2. Start the stack:

   ```bash
   docker compose -f compose.selfhost.yaml up --build -d
   ```

3. Open <http://localhost:8088> (or the port configured by `HTTP_PORT`). Check `docker compose -f compose.selfhost.yaml ps` and `docker compose -f compose.selfhost.yaml logs -f api frontend worker` for health and startup errors.
4. Stop the stack with `docker compose -f compose.selfhost.yaml down`. This preserves named data volumes. To remove volumes as well, `docker compose -f compose.selfhost.yaml down -v` permanently deletes the local database/broker data.

Self-hosting notes:

- Expose only the frontend/reverse-proxy port to the LAN or internet. Do not publish PostgreSQL or RabbitMQ ports publicly.
- Put TLS in front of the app before exposing it to the internet; configure firewall rules, host patching, secret rotation, and tested backups.
- PostgreSQL and RabbitMQ containers are included as infrastructure, but **the current application does not yet persist orders to PostgreSQL or publish/consume RabbitMQ events**. The worker is a scaffold, not an order processor. The database volume alone does not make orders durable.
- The self-host profile is therefore suitable for verifying container wiring and a demo, not yet for handling real customer orders.

### Cloud portability

Keep application images provider-neutral and configuration in environment variables/secrets. Once a cloud is selected, choose a managed container runtime, managed PostgreSQL, and an appropriate managed queue or RabbitMQ hosting option; add registry publishing, secret integration, TLS/domain configuration, backups, monitoring, and deployment/rollback automation. Do not commit to Azure, AWS, or Google Cloud-specific services until the deployment requirements and budget are clearer.

## First Git repository upload

This repository is intended to contain **source code, tests, deployment/configuration files, developer instructions, and CI workflows**—not generated build output, local secrets, or runtime data.

### Files to include

Commit the project files that are part of the repository, including:

- `README.md`, `.gitignore`, `.gitattributes`, `.editorconfig`, `global.json`, and `.env.example`.
- `src/`, `tests/`, `deploy/`, `scripts/`, and the Compose files.
- `.github/workflows/` so CI runs on the remote repository.
- `.vscode/settings.json` for shared workspace formatting preferences (personal extensions and machine-specific settings should not be committed).
- `src/frontend/package-lock.json` **after generating it with a successful npm install**. Commit the lockfile so dependency versions are reproducible; do not commit `node_modules/`.

### Do not upload

The root `.gitignore` excludes common generated or local-only content, including `.env` and other environment-specific secret files, .NET `bin/` and `obj/`, test results, Node `node_modules/`, frontend `dist/`, coverage output, IDE state, and OS metadata. Keep `.env.example` as a template with placeholders only. Do not commit passwords, tokens, production connection strings, database dumps, RabbitMQ/PostgreSQL data volumes, logs, or exported artifacts.

Dockerfiles, Compose manifests, source files, tests, and CI configuration are **not artifacts**; they are required project inputs and should be committed. Do not commit ZIP archives of the project inside the repository.

### Prepare and inspect the first commit

Before staging, stop local services if desired and ensure `.env` contains only local settings. From the repository root in PowerShell:

```powershell
# Generate the frontend lockfile (requires network access to npm).
cd src/frontend
npm install --no-audit --no-fund
cd ../..

# Review what Git sees before staging anything.
git init
git status --short
git check-ignore -v .env src/backend/LongPizzaOrderBuilder.Api/obj src/frontend/node_modules

# Stage the project and inspect the exact upload set.
git add .
git status --short
git diff --cached --stat
git diff --cached --check
git diff --cached
```

**Do not commit until you have inspected the staged diff.** Confirm that `.env`, credentials, generated directories, local database data, and ZIP files are absent. If a secret is accidentally staged, unstage it and remove it from the index before committing; if it was already committed or pushed, rotate the secret because deleting it in a later commit does not erase history.

Once the staged files look correct, make the initial commit:

```powershell
git commit -m "Initial LongPizzaOrderBuilder project"
```

Create an empty repository on your Git hosting provider (for example, GitHub); do not initialize it with a second README or license if you already have those locally. Then add the provider's repository URL and push the initial branch using the commands shown by that provider. For GitHub, the usual shape is:

```powershell
git branch -M main
git remote add origin https://github.com/YOUR-ACCOUNT/LongPizzaOrderBuilder.git
git push -u origin main
```

Replace the example URL with your real repository URL. Review repository visibility and access before pushing; choose private if you do not intend the code and project history to be public.

### First-upload verification checklist

- [ ] `.env.example` contains placeholders only; `.env` is ignored.
- [ ] `.gitignore`, `.gitattributes`, `.editorconfig`, and `global.json` are present.
- [ ] `src/frontend/package-lock.json` is committed; `node_modules/` is not.
- [ ] Source, tests, Docker/Compose configuration, scripts, README, and CI workflow are included.
- [ ] No build output, test results, logs, database volumes/dumps, secrets, or project ZIP archives are staged.
- [ ] `git diff --cached --check` reports no whitespace errors.
- [ ] The first commit is created only after reviewing the staged diff.
- [ ] After pushing, the GitHub Actions workflow result is checked and recorded in this README.

## Roadmap

### P0 — align current prototype with agreed business rules

- [x] Replace prototype prices with the agreed $5 / $7 / $12 sizes and 8% / 11% topping rates in the API menu/pricing service.
- [x] Implement tax, tips, promotions, the strict `SAVE5` threshold, and a documented two-decimal rounding policy in the pricing service.
- [x] Add `POST /api/pricing/preview` and connect the React UI to the server-calculated preview.
- [x] Drive displayed size/topping choices from `GET /api/menu`.
- [x] Add validation for invalid options, duplicate toppings, invalid quantities, and customer name.
- [x] Add pricing unit tests and API integration tests in source; successful execution is still pending verification.

### P1 — reliable order lifecycle

- [ ] Add PostgreSQL persistence with EF Core entities, migrations, constraints, and transaction boundaries.
- [ ] Save immutable order/line-item pricing snapshots.
- [ ] Extend API tests as persistence, lifecycle, and messaging are implemented.
- [ ] Publish a versioned `PizzaOrderPlaced` event using a transactional outbox.
- [ ] Consume events in the worker with durable queues, retry/backoff, dead-letter handling, and idempotency.

### P2 — production readiness

- [ ] Add authentication and authorization where required, request validation, rate limiting, and secure CORS configuration.
- [ ] Define payment, inventory, delivery-fee, tax jurisdiction, refund, and order cancellation behavior.
- [ ] Add structured logs, metrics, distributed tracing, readiness/liveness checks, alerts, and backup/recovery procedures.
- [ ] Harden CI/CD with dependency and container scanning, immutable image tags, pinned third-party actions, registry publishing, cloud identity federation, deployment approvals, and rollback strategy.
- [ ] Run and record successful frontend, backend, unit, integration, and container build checks.

## Order builder, running total, description, and lifecycle requirements

This section records the product behavior and the invariants that every implementation must preserve. These are acceptance requirements, not a claim that the current prototype already implements them.

### Order builder and line items

- An order contains **one or more line items** at submission time. An empty draft is allowed while the customer is building an order, but it cannot be submitted.
- A line item represents one product/configuration and its quantity. For a pizza, the configuration includes product, size, crust where available, selected toppings/customizations, and any future item-specific options.
- Quantity belongs to the line item when multiple identical configurations are ordered. Quantity must be a positive integer within a documented server-side limit. Changing quantity updates the line total and order totals.
- Different configurations are different line items. If two line items have exactly the same normalized configuration, the UI may offer to merge them, but merging is a UX choice and must not change the calculated total.
- Each selected option must be valid for the chosen product and size according to the current menu. Unknown, unavailable, incompatible, or duplicate options must be rejected by the API; the client cannot make an invalid option valid by submitting it.
- The client maintains a draft configuration for interaction. The server validates the configuration on preview and again on submission.
- Removing a line item removes its entire contribution (base price, modifiers, and quantity) from the draft subtotal. If removal leaves the draft empty, the UI shows an empty-order state and disables checkout; it must not show a negative total or submit an empty order.

### Running total and repricing behavior

- Every meaningful change to size, product, option selection, option quantity, line-item quantity, item addition/removal, promotion, or tip triggers a refreshed preview.
- The displayed breakdown should include item/line totals, subtotal before discounts, applied discount, taxable subtotal, tax, tip, and final total. Savings should be clearly visible when a promotion is applied.
- The server pricing service is authoritative. React may display a pending state while the preview request is in flight; it must not present a stale preview as if it matches the latest selections. Debounce requests or cancel/ignore stale responses using a request/version key.
- Preview and order submission must use the same pricing rules. Submission recalculates everything server-side; client-provided amounts are never trusted.
- If a line item is added, changed, or removed, the subtotal, promotion eligibility, discount, taxable subtotal, tax, percentage tip, and total must all be recalculated from the current draft.
- If a promotion is entered but its conditions are not met, keep the customer's entered code visible, mark it as **not applied**, explain the specific unmet condition, and calculate the total without its discount. Do not silently apply a partial or invalid discount. For `SAVE5`, the initial rule is strictly greater than $20 on the eligible subtotal before discounts and tax; exactly $20 does not qualify.
- `ILOVEPIZZA` and `SAVE5` cannot be combined. If a customer attempts to apply a second incompatible code, explain the conflict and require a clear choice of which code to keep. Do not silently replace an existing code without communicating it.
- If an applied promotion becomes ineligible after the customer changes or removes items, re-evaluate it immediately, show that it is no longer applied and why, and remove its discount from the preview. The customer may add items or choose another eligible promotion.
- A useful future enhancement is a **best-value promotion recommendation**. It may compare eligible promotions and show potential savings, but it must use the same server-side promotion evaluator. Recommendation is distinct from automatic application; do not change the customer's selected code without clear UI feedback.

### Tip behavior

- The customer can choose no tip, one of the UI's percentage options, or a custom currency amount. Percentage choices should be configurable by the product/UI rather than encoded as pricing logic.
- Tip is recalculated whenever the tip selection or the amount it is based on changes. A custom amount remains a fixed currency amount when the order subtotal changes; a percentage tip changes with its configured base.
- Current working rule: percentage tip is calculated on the discounted, post-tax amount. This must be confirmed as a business policy before production. The custom tip is an explicit amount and is not multiplied by the order quantity.
- The UI must display the selected tip mode and calculated amount. Validation must reject negative values, unsupported percentages, malformed values, and values above configured limits.
- If the order becomes empty, tip and total must return to zero and checkout must be disabled. Do not retain a tip on an empty order.

### On-demand order description

- The detailed human-readable order description is **not displayed by default**. The customer can request it using an explicit action such as **View order details**.
- That action opens an accessible modal/dialog containing a readable summary of each line item: product, size, crust/options, toppings/customizations, quantity, line total, promotion, discount, tax, tip, and grand total. For a draft, label it as an estimate; for an accepted order, show the persisted receipt snapshot.
- The dialog should support keyboard focus management, Escape/close behavior, a visible close control, and an appropriate accessible name. Closing it must not clear or mutate the order.
- Provide an explicit next action only if implemented (for example, Copy summary or Print). Do not imply that opening the dialog submits an order. The summary must be generated from the current draft/preview or accepted receipt, not from independently reimplemented frontend arithmetic.
- If a dialog is not appropriate on a small screen, the same explicit action may open a full-screen dialog/page while preserving the requirement that the detailed description is only surfaced on request.

### Order submission and lifecycle UX

The order lifecycle should be represented explicitly rather than inferred from whether an API request succeeded. Suggested initial states:

- **Draft** — editable local/customer configuration; not yet submitted.
- **Submitted** — accepted by the API and persisted; return an order ID and accepted price snapshot.
- **InProgress** — the order is being prepared/fulfilled.
- **Delayed** — fulfillment is later than the current estimate; include a customer-safe reason when known and a revised ETA if available.
- **Completed** — fulfillment is complete.
- **Cancelled** — the order will not be fulfilled; include a reason where appropriate and define refund/payment behavior before enabling cancellations in production.

Lifecycle requirements:

- Only the server may accept an order and assign its durable order ID, accepted total, and initial lifecycle state. A frontend timeout is an **unknown outcome**, not proof that the order failed; the UI should allow status lookup/retry using an idempotency key rather than blindly creating a duplicate order.
- On successful submission, show a confirmation page/state with order ID, accepted receipt total, status, and ETA when the service has one. Do not invent an ETA; represent unavailable ETA as “Not available yet” or equivalent.
- Display status with both text and a visual indicator; do not rely on color alone. Refresh status through polling or a supported push channel and handle temporary connectivity failures without falsely changing the order state.
- **InProgress** should show the latest ETA when available. **Delayed** should clearly communicate the delay, revised ETA when known, and a safe explanation when known. Do not expose internal operational notes or personal data in customer-facing reasons.
- **Completed** should clearly state that the order is complete and preserve access to the accepted receipt. **Cancelled** should state that the order was cancelled and clearly communicate refund/payment status when payment functionality exists.
- Define allowed state transitions on the server. A suggested initial transition graph is `Submitted -> InProgress -> Completed`, with `InProgress -> Delayed -> InProgress` (or `Delayed -> Completed`) as appropriate, and cancellation only from explicitly allowed states. Terminal states `Completed` and `Cancelled` cannot silently transition back to active states. Exact cancellation rules and whether `Submitted -> Cancelled` is permitted require product policy.
- Store lifecycle timestamps and status history (previous state, new state, timestamp, actor/source, and reason) for support and auditability. Event consumers must be idempotent so duplicate messages do not duplicate transitions or customer notifications.
- Lifecycle events such as `PizzaOrderSubmitted`, `PizzaOrderStarted`, `PizzaOrderDelayed`, `PizzaOrderCompleted`, and `PizzaOrderCancelled` are proposed integration events. Publish them reliably after persistence (transactional outbox); this event flow is planned, not currently implemented.

## Critical invariants

These invariants must hold regardless of UI, API, worker, retry, or deployment path. Treat failures as defects, not merely edge cases.

1. **Non-empty accepted order:** an accepted order contains at least one valid line item; every line-item quantity is a positive integer within the configured limit.
2. **Valid configuration:** every product, size, and selected customization exists, is available, and is compatible. The server ignores client-supplied prices and recalculates from trusted menu/pricing data.
3. **One pricing source of truth:** preview and submission call the same pricing logic and rounding policy. If the menu/rules change between preview and submission, the accepted order uses the current validated rules and returns its authoritative receipt.
4. **Correct arithmetic:** line totals equal unit/configuration price multiplied by quantity; subtotals equal the sum of line totals; discounts cannot exceed their eligible subtotal; taxable subtotal, tax, tip, and final total reconcile according to the documented order of operations.
5. **Promotion eligibility and exclusivity:** `SAVE5` applies only when the eligible pre-discount subtotal is strictly greater than $20; `ILOVEPIZZA` is 10% off the pre-tax subtotal; incompatible promotions are never combined; invalid/ineligible codes never reduce the total.
6. **Consistent recomputation:** adding, editing, merging, or removing an item causes all dependent amounts and promotion eligibility to be recomputed. No stale asynchronous preview may replace a newer result.
7. **Tip correctness:** no tip means $0; a percentage tip follows the configured base and updates when that base changes; a custom tip remains the entered currency amount; invalid/negative values are rejected.
8. **Money precision:** use decimal/fixed-precision numeric storage, a single documented rounding policy, and tests at half-cent boundaries. Avoid binary floating-point for backend monetary calculations.
9. **Receipt immutability:** once accepted, the order's option labels, price components, promotion, tax, tip, and total remain stable even when the catalog or promotion definitions change later.
10. **Submission idempotency:** repeating the same submission request with the same idempotency key returns the same accepted order instead of creating duplicates. A failed/uncertain network response must be safely recoverable.
11. **Valid lifecycle transitions:** state changes are checked on the server, recorded with timestamps and reasons where applicable, and terminal states cannot be reopened through ordinary transitions.
12. **Reliable event effects:** a persisted state change and its outgoing event cannot silently diverge; duplicate delivery does not duplicate side effects.
13. **Customer-visible truth:** status, ETA, discount messages, and order descriptions reflect server responses and persisted data. Unknown values are shown as unknown, not guessed.
14. **Accessible detail dialog:** order description appears only after explicit request, is keyboard accessible, and opening/closing it does not mutate or submit the order.

## Automated testing requirements

Implement tests at three levels: fast domain unit tests, API/DB integration tests, and frontend component/interaction tests. Add a smaller end-to-end suite that exercises the critical customer journey against the real containerized stack. Tests should be deterministic and run in CI.

### P0 — domain and pricing unit tests

- [ ] Base prices are exactly $5, $7, and $12 for Small, Medium, and Large.
- [ ] Each basic topping costs 8% of the selected pizza base price; each premium topping costs 11%; rates are calculated from base price, not from the growing subtotal.
- [ ] Multiple toppings sum correctly; multiple quantities multiply the correct line amount; topping quantities follow the configured rule.
- [ ] Empty orders, zero/negative/too-large quantities, duplicate toppings where prohibited, unknown IDs, and incompatible options are rejected.
- [ ] `ILOVEPIZZA` discounts 10% of the eligible pre-tax subtotal; `SAVE5` applies $5 when subtotal is greater than $20 and does not apply at exactly $20 or below.
- [ ] The two promo codes cannot be combined; unknown, inactive, expired, and ineligible promotions do not discount the order and return meaningful reasons.
- [ ] Tax is calculated from the discounted taxable subtotal and excludes tip.
- [ ] No tip, percentage tip, and custom tip are calculated correctly; test lower/upper bounds and recalculation after cart changes.
- [ ] Rounding tests cover half-cent boundaries, many line items, discounts, tax, and totals using the single documented rounding strategy.
- [ ] Property/invariant tests assert total reconciliation, non-negative discount/tax/tip where valid, and that discount never exceeds eligible subtotal.
- [ ] Boundary tests cover values just below, exactly at, and just above $20 and every configured validation limit.

### P0 — API integration tests

- [ ] `GET /api/menu` returns the options and prices used by the server pricing service.
- [ ] `POST /api/pricing/preview` returns a complete breakdown and does not persist an order.
- [ ] Preview rejects malformed or incompatible configurations with structured, customer-safe validation errors.
- [ ] `POST /api/orders` recalculates prices server-side even if the request attempts to include or tamper with client totals.
- [ ] The accepted order receipt matches the server-calculated preview when menu and rules are unchanged.
- [ ] A menu/rule change between preview and submission is handled explicitly; the accepted response returns the authoritative price rather than trusting the old preview.
- [ ] Order creation persists line items and immutable price snapshots in one transaction.
- [ ] Repeated requests with the same idempotency key do not create duplicate orders; reusing a key with a materially different request is rejected.
- [ ] Status retrieval returns the correct lifecycle state, ETA availability, and safe delay/cancellation reasons.

### P0 — frontend tests

- [ ] Adding a line item creates the expected configuration and quantity; changing size/options/quantity updates the preview.
- [ ] Removing a line item removes its full price contribution; removing the last item produces the empty state, zero totals, and disabled checkout.
- [ ] Preview responses arriving out of order cannot overwrite the newest configuration's total.
- [ ] Promotion states distinguish applied, not applicable, invalid, and conflicting codes; explanations identify the unmet condition.
- [ ] If an edit makes a previously applied promo ineligible, the UI removes the discount and explains why.
- [ ] Tip selection updates correctly for no tip, percentage, and custom amount as cart values change.
- [ ] The detailed order description is hidden by default and appears only when requested; dialog content matches the current preview/receipt; close and Escape do not mutate the order.
- [ ] Submission success shows the order ID, accepted total, lifecycle status, and ETA only when provided.
- [ ] Timeout/connection errors do not falsely claim the order failed or encourage duplicate submissions; retry uses the idempotency key.
- [ ] Status changes display accessible text as well as visual indicators; delayed/cancelled states show customer-safe messages.
- [ ] Keyboard-only interaction, focus return, dialog labeling, and screen-reader announcements are tested for critical flows.

### P1 — lifecycle, persistence, and messaging tests

- [ ] Valid lifecycle transitions succeed; invalid transitions and attempts to reopen terminal states are rejected.
- [ ] Delayed transitions preserve a reason and revised ETA when supplied; unknown ETA remains explicitly unknown.
- [ ] Status history records state, timestamp, actor/source, and reason.
- [ ] Order persistence survives API restarts and receipt snapshots do not change when catalog prices change.
- [ ] The transactional outbox records an event atomically with the order/state update; dispatch retries eventual publication.
- [ ] Worker retries and duplicate event deliveries are idempotent; poison messages are routed to a dead-letter queue and observable.
- [ ] Cancellation/payment behavior is tested only after the refund and payment policy is defined.

### P1 — end-to-end scenarios

- [ ] Build a pizza with multiple toppings, add a second line item, change quantity, remove an item, and verify the running total at every step.
- [ ] Apply `ILOVEPIZZA`; verify discount, tax, tip, and total. Attempt `SAVE5` and verify incompatibility is explained.
- [ ] Build a subtotal of exactly $20 and verify `SAVE5` does not apply; increase it above $20 and verify it applies.
- [ ] Apply `SAVE5`, then remove items so the subtotal drops to $20 or below; verify the discount is removed and the reason is displayed.
- [ ] Open and close the order-description dialog and verify it has no side effects.
- [ ] Submit an order, refresh/reopen its status, and exercise InProgress, Delayed, Completed, and permitted Cancelled flows with correct ETA/reason presentation.
- [ ] Simulate a lost response after submission and retry safely without creating a second order.

### Test implementation guidance

- Use xUnit (or the team's chosen .NET test framework) for domain and API tests, and a React testing library with a browser-level tool such as Playwright for interaction and end-to-end tests.
- Use a disposable PostgreSQL and RabbitMQ instance for integration tests that exercise persistence/outbox/consumer behavior; do not replace these tests with mocks alone.
- Keep pricing unit tests fast and run them on every pull request. Run containerized integration and end-to-end tests in CI with reliable cleanup and isolated data.
- Seed tests with fixed menu and promotion data. Avoid wall-clock-dependent expiration tests without a controllable clock.
- Record test commands and actual CI results here only after they have been run. A test checklist is a plan until the corresponding test exists and passes.

## Decisions still requiring product confirmation

- Confirm that percentage tips are based on the discounted post-tax total; decide whether tip presets are 10% / 15% / 20% or another set.
- Confirm whether a customer can edit or cancel an order after submission, and define allowed cancellation states, refunds, and payment authorization/capture behavior.
- Define how ETA is estimated, how often it refreshes, and which delay reasons are safe to show customers.
- Confirm the configured maximum line-item quantity, custom-tip limit, and whether duplicate toppings or multiple portions are allowed.
- Decide whether promotion recommendations merely suggest the best code or automatically apply it.
- Define the menu/rule versioning policy when prices change between preview and submission.

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

### Next revision — planned, not yet implemented

- Run `scripts/verify.ps1` (Windows) or `scripts/verify.sh` (macOS/Linux), and confirm the GitHub Actions workflow passes; record the actual results.
- Replace in-memory order storage with PostgreSQL/EF Core migrations and durable order/receipt snapshots.
- Implement submission idempotency and a transactional outbox; connect RabbitMQ and replace the worker heartbeat with real consumers.
- Add a browser end-to-end test for a full build-preview-submit-retrieve journey.
- Revisit deployment security, TLS, backup/restore, and CI registry publishing before internet-facing use.
