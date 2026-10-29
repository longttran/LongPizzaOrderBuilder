# AGENTS.md

Guidance for AI coding agents and new contributors. `CLAUDE.md` imports this file, so keep it the single source.

## What this is
LongPizzaOrderBuilder: customers build one or more pizzas, watch a running total, and send the order to a **separate order service** that holds the order and its lifecycle
(Not Started, Started, Ready for Pickup, Delivered, Cancelled). React + TypeScript page, two ASP.NET Core (.NET 10) back ends, and a shared pricing library. There is no database or message broker (RabbitMQ and PostgreSQL were removed from Compose because nothing used them); orders are held in the order service's
memory. Treat it as a prototype, not a production ordering system. Scope: one restaurant, anonymous customers, no auth, payments, kitchen integration, menu admin or multiple locations.

## Layout
- `src/backend/LongPizzaOrderBuilder.Pricing`: the menu data (`PizzaMenu`) and `PricingService`, the only place prices are calculated. Used by both back ends.
- `src/backend/LongPizzaOrderBuilder.Api` (:8080): the order builder's back end. Menu and price preview only. No order endpoints.
- `src/backend/LongPizzaOrderBuilder.OrderService` (:8090): a separate process. `OrderStore.cs` (orders, idempotency keys, `Transition`), `OrderLifecycle.cs` (statuses and the transition rules),
  `Controllers/OrdersController.cs` (the customer's door), `Controllers/RestaurantController.cs` (the restaurant's door), `KitchenSimulator.cs` (optional timer).
- `src/frontend/src`: `domain/` (pure cart state), `hooks/` (`usePricingPreview`, `useOrderTracking`), `components/`, `api/` (talks to both back ends), `test/` (fixtures).
- `tests/…UnitTests`, `tests/…IntegrationTests`: xunit. The integration tests start each back end as a real HTTP server.
- `docs/INVARIANTS.md`: the rules tests must protect. `docs/decisions/`: why things are the way they are. `docs/LIFECYCLE.md`: how orders move and what is guaranteed.

## Commands
```bash
node scripts/check-invariants.mjs                         # registry and tests agree
dotnet test LongPizzaOrderBuilder.slnx                    # all backend tests
scripts/dev.sh                                            # run the page, both back ends together
scripts/demo-order-service.sh                             # a retry after a timeout, and cancel-versus-start, against a running service
cd src/frontend && npm ci && npm run typecheck && npm run lint && npm test && npm run build
```
Use `npm ci` (lockfile) rather than `npm install`. Pin new dependencies to exact versions, as the repo does.

## Rules that matter
1. **Money is decided on the server only.** Never compute, sum, multiply or round a price in the frontend. Render what
   `breakdown` returns. Never send prices to the API. (ADR-0001, UI-001, ORD-002)
2. **Use `decimal` for money in C#.** No `double`/`float`. Round with `MidpointRounding.AwayFromZero` through the service's `Money()`.
3. **Pricing rules are data, and there is one engine.** Change prices, rates, tax or promotions in `PizzaMenu`, not by branching in `PricingService`; never copy pricing code into a service. The order service prices every order and update itself and never trusts a total from a caller. (ADR-0005, ADR-0009)
4. **A displayed price must be current.** Do not show an old total as if it matched the order. Use `usePricingPreview`. (ADR-0003)
5. **Every behaviour change touching an invariant updates `docs/INVARIANTS.md` and its tests in the same change.**
   Tests cite IDs (`[Trait("Invariant","PRC-004")]` or `describe('UI-002: …')`). Do not mark an invariant `enforced` without a test.
6. **Prove a new test can fail.** Break the code on purpose and confirm the test goes red before trusting it.
7. **Record non-obvious decisions** as a short ADR in `docs/decisions/`. Do not edit an accepted ADR; supersede it.
8. **Order status changes only through `OrderStore.Transition`.** `Order` is an immutable record; never edit one in place, and never read the status and write it back outside the store's lock. The legal moves are the one table in `OrderLifecycle`. The customer cancels only while Not Started; the restaurant moves one step forward and cannot cancel. (ADR-0007, ADR-0009, LIFE-001 to LIFE-005). Changing a placed order is deliberately not built; do not add it without the decisions in `docs/FUTURE-WORK.md`.
9. **Retries must be safe.** Create requires an `Idempotency-Key` that the page keeps across retries of the same order; the service must never create a second order for a key. (ADR-0008, ORD-004, UI-015)
10. **Test races deterministically.** Use `PausingClock` to freeze a request inside the store's critical section; do not rely on thread timing (timing tests passed even with the lock removed on a one-core machine). A refused request returns 409 or 412 with the current order, and the UI shows it.
11. **Contract changes** (menu or order JSON) need a decision first; see ADR-0006 and ADR-0010. Prefer additive fields.

## Conventions
- Editor settings are in `.editorconfig` (4 spaces C#, 2 spaces TS/JSON/YAML). Keep final newlines.
- Frontend: small components, state transitions in the pure reducer in `domain/cart.ts`, no price fields in state.
- Test fixtures (`src/test/fixtures.ts`) use deliberately unrealistic prices so a UI that computes its own would fail.
- The test projects reference both back ends, so every entry-point type needs a distinct name. The builder API's is `Program`; the order service's is `OrderServiceProgram` (two types called `Program` in two referenced assemblies is compile error CS0433). Do not use top-level statements in a new back end.
- Tests must satisfy the xunit analyzers: use `Assert.Empty` / `Assert.Single` rather than `Assert.Equal(0|1, x.Count)`, `Assert.Contains` rather than `Assert.True(x.Contains(...))`, and `async`/`await` rather than `Wait`, `WaitAll` or `.Result`.
- Verify the tests as separate assemblies. A check that compiles everything into one assembly hides type-name collisions.
- Accessibility is a requirement: keyboard operation, labelled controls, visible focus, no information by colour alone.

## Do not
- Commit `.env`, secrets, `bin/`, `obj/`, `node_modules/`, `dist/`.
- Claim something is verified unless you ran it. Say which parts were run and which were only read.
- Add authentication, payments, kitchen integration, a menu admin UI or multiple locations; they are out of scope. Persistence and messaging are described in ADR-0010; do not start them without a decision.

## Known gaps (do not rely on these)
No persistence (orders and idempotency keys are lost on restart), one order-service instance only (the lock is in-process), no authentication (the restaurant routes are open to anyone who can reach the service), status by polling, and no menu version check between the builder and the service. ESLint 9 is used; ESLint 10 is current, so plan an upgrade once typescript-eslint support is confirmed. See `docs/decisions/ADR-0010-decisions-outside-the-scope.md`.
