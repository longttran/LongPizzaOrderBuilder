# Design notes

How LongPizzaOrderBuilder is designed, what it assumes, and what it does not do yet. Each claim says whether it is
**built and tested**, **built but not tested**, or **designed only**. Decisions and rejected alternatives are in
[`decisions/`](decisions/README.md); the rules tests protect are in [`INVARIANTS.md`](INVARIANTS.md).

## 1. Scope

The system is one restaurant with anonymous customers. These constraints were checked against the repository.

| Constraint | Status | Evidence |
|---|---|---|
| No authentication | Met | No auth code anywhere in `src/` or `tests/`. The customer gives a name for the order and nothing else. |
| No customer accounts | Met | No user model, sessions or login UI. |
| No real payments | Met | The tip is a number added to the total. No payment provider, card fields or charge calls. |
| No kitchen or delivery integration | Met in code, not in infrastructure | No code sends orders to a kitchen. The restaurant side is simulated (an endpoint, `scripts/restaurant.sh`, an optional timer). The Compose stack still starts a `worker`, RabbitMQ and PostgreSQL that nothing uses (ADR-0010). |
| No menu administration UI | Met | The menu is read-only data. |
| No multiple locations | Met | One menu, one tax rate, one store. |
| Prioritise correct pricing | Met | Section 4. |
| Prioritise a defensible order lifecycle | Met | Explicit statuses and transition table, recorded history, safe retries and race handling. See [LIFECYCLE.md](LIFECYCLE.md). |

## 2. Assumptions

Each assumption is implemented in one place and checked by a test where marked. Items 3, 15 and 16 are the ones that most need a
business owner to confirm.

| # | Assumption | Where | Tested |
|---|---|---|---|
| 1 | One restaurant, one currency (USD), anonymous customers. | whole system | n/a |
| 2 | Crust does not change the price. | `PricingService` | PRC-001 |
| 3 | **A percentage tip is a percentage of (discounted subtotal + tax).** The brief does not say which base. A custom tip is a fixed amount. Neither tip is taxed or discounted. | `CalculateTip` | PRC-009, PRC-010 |
| 4 | A topping costs its rate × the **base price of the pizza it is on** (8% basic, 11% premium), not of the running subtotal. | `PricingService` | PRC-002 |
| 5 | Each topping can be on a pizza once. Quantity multiplies the whole pizza, toppings included. | `Validate`, cart reducer | PRC-003, PRC-014, UI-013 |
| 6 | One promotion per order, entered as one code. Two codes cannot be combined because the request has one field. Codes ignore case and surrounding spaces. | `EvaluatePromotion` | PRC-007 |
| 7 | `SAVE5` needs a subtotal strictly greater than $20.00 **before** discount and tax. `ILOVEPIZZA` is 10% of the pre-tax subtotal. | menu data | PRC-005, PRC-006 |
| 8 | An unknown or ineligible code never fails the order. It is reported as "not applied" with a reason and costs nothing. | `EvaluatePromotion` | PRC-007, UI-007 |
| 9 | Tax is 6.5% of the discounted subtotal, charged once for the whole order. | `PricingService` | PRC-009 |
| 10 | Rounding is half away from zero to cents, applied at every money step (each topping, line, discount, tax, tip, total). The parts shown to the customer therefore always add up to the total. | `Money()` | PRC-011, PRC-012 |
| 11 | Limits: quantity 1 to 10 per line, at most 50 lines, percent tip 0 to 100, custom tip $0 to $10,000, name up to 80 characters. A custom tip with more than two decimals is rounded to cents. | `Validate`, `OrdersController` | PRC-014 |
| 12 | Identical lines are not merged. Merging would not change the total. | n/a | n/a |
| 13 | An empty order has no price and cannot be submitted. The UI shows $0.00. | `Validate`, UI | PRC-014, ORD-001, UI-004 |
| 14 | An order is priced by the order service when it is sent, with the shared pricing library. The builder's preview may be out of date if the menu changed; the service's price is the one that counts, and it never changes after the order is placed. | `OrderStore` | ORD-002, ORD-003 |
| 15 | **Orders and idempotency keys live in the order service's memory and are lost on restart.** This is a prototype limit, not a design goal (ADR-0010). | `OrderStore` | n/a |
| 16 | **A customer may cancel only while the order is Not Started.** After the restaurant starts, cancelling is refused. This is a business rule to confirm with the restaurant (it comes from the brief). **Changing a placed order is deliberately not built:** a customer who wants a change cancels and places a new order. See [FUTURE-WORK.md](FUTURE-WORK.md). | `OrderLifecycle` | LIFE-004 |
| 17 | The restaurant's routes are open to anyone who can reach the service, because there is no authentication. The caller's role comes from the door (`/api/orders` or `/api/restaurant/orders`), not from the request. | `RestaurantController` | API-003 |
| 18 | The statuses are Not Started, Started, Ready for Pickup and Delivered, plus Cancelled as a final status so a cancelled order can say so. The restaurant moves an order one step forward and cannot cancel. | `OrderLifecycle` | LIFE-001, LIFE-004 |
| 19 | Asking for the status an order already has succeeds and changes nothing. | `OrderStore` | LIFE-005 |
| 20 | An idempotency key is **required** on create and is remembered for the life of the process; a request that failed validation does not use up its key. | `OrderStore` | ORD-004, API-004 |

## 3. Data modelling

Menu, order, line item and status are separate concepts with separate types.

| Concept | Type | Contents | Notes |
|---|---|---|---|
| Menu | `PizzaMenu` | sizes with base price, crusts, toppings (id, name, category, rate), tax rate, promotions | Read-only data. Lives in the shared `Pricing` library and is injected into `PricingService` in both back ends. |
| Promotion | `PromotionDescription` | code, type (`Percentage` or `FixedAmount`), rate or amount, optional `MinimumSubtotalExclusive` | The rule is data. Only the *type* is code. |
| Line item (what the customer chose) | `PizzaOrderItemRequest` | size, crust, toppings, quantity | Choices only, never prices. |
| Line item (what it costs) | `PricedOrderLine` | base price, per-topping prices, unit price, line total | Produced by the pricing engine. |
| Order | `Order` | id, customer name, items, `PricingBreakdown`, status, history, times | Lives in the order service. Holds the priced breakdown as accepted, which never changes. |
| Order status | `OrderStatus` enum, `OrderEvent` history, `OrderActor` | NotStarted, Started, ReadyForPickup, Delivered, Cancelled; each change records what happened, the status, the time and who did it | The rules are one table in `OrderLifecycle`. `Order` is an immutable snapshot, so a change makes a new one. |

Pricing comes from data. Prices, topping rates, the tax rate, promotion rules and thresholds are read from the menu, and tests
change the menu to prove it (PRC-015).

Constants that remain in code, listed so none is hidden: `MaxQuantityPerLine = 10`, `MaxLinesPerOrder = 50`, the tip bounds and the
rounding mode. The frontend mirrors the first two (`MAX_QUANTITY`, `MAX_LINES`) and the tip percentage buttons. Exposing the limits
through `GET /api/menu` would remove the duplicates (ADR-0006).

Two honest weaknesses: the default menu is compiled into the shared library, so changing it means a build and deploy of both back ends; and sizes and
crusts are identified by display name, while toppings have ids.

## 4. Pricing

**One source of truth.** `PricingService.Calculate`, in the shared `LongPizzaOrderBuilder.Pricing` library, is the only code that produces a price. The builder's preview and the order service (when it receives an order) both call it. The browser formats numbers the server returns and does no price arithmetic (UI-001, ADR-0001).

**Exact arithmetic.** The server uses `decimal` throughout. Amounts reach the browser as JSON numbers and are only displayed. No `double` is used for money.

**Order of operations**
1. Each topping = rate × the pizza's base price, rounded to cents.
2. Unit price = base + toppings. Line total = unit price × quantity.
3. Subtotal = sum of line totals.
4. Promotion: at most one; the discount is capped at the subtotal.
5. Tax = 6.5% × (subtotal − discount).
6. Tip = none, a percentage of (subtotal − discount + tax), or the custom amount.
7. Total = (subtotal − discount) + tax + tip.

**Consistent rounding.** One function, `Money()`, rounds half away from zero. Every step above passes through it, so there is no
hidden fractional cent and the displayed parts sum to the displayed total (PRC-011, PRC-012). Banker's rounding would give $0.32 for
tax on $5.00; the rule gives $0.33, and a test pins that.

**Checked independently.** The pricing tests compare about 20,000 generated orders with a second implementation written in integer
cents that shares no code with the service, so one mistake cannot appear in both.

## 5. Service design

Two back ends, as separate processes (ADR-0009): the **order builder API** (menu and price preview, no state) and the **order service** (orders, lifecycle, the restaurant's side).

| Method and path | Process | Purpose |
|---|---|---|
| `GET /api/menu`, `POST /api/pricing/preview` | builder API (:8080) | The menu, and a price for a draft. Safe to repeat. Invalid input gives 400 with the reasons. |
| `POST /api/orders` | order service (:8090) | Send an order. Requires `Idempotency-Key`: 201 new, 200 repeat, 422 key reused for a different order. Reprices. |
| `GET /api/orders/{id}` | order service | Status, history, `canCustomerCancel`. |
| `POST /api/orders/{id}/cancel` | order service | Cancel, only while Not Started. 200 (also if already cancelled) or 409 with the order as it is. |
| `GET /api/restaurant/orders`, `POST /api/restaurant/orders/{id}/transitions` | order service | The restaurant sees orders and moves one step forward. No authentication. |

**Server-side validation (built, tested).** Both back ends validate with the same engine and never trust the client: unknown size, crust or topping, duplicate topping, quantity outside 1 to 10, more than 50 lines, a bad tip,
a blank name, a missing or malformed idempotency key, an unknown status. The browser sends choices only and the service ignores any money fields (UI-009, ORD-002).

**Safe retries (built, tested).** A retry carries the same `Idempotency-Key` and gets the original order, never a second one, even for simultaneous duplicates (ORD-004, UI-015). Cancelling and the restaurant's moves are safe to repeat (LIFE-005).
The page times out after 10 seconds and says the order "may or may not have been received", and that pressing the button again is safe.

**Explicit order state (built, tested).** `OrderStatus` is an enum with one transition table. `OrderStore.Transition` is the only way to change an order, and it checks the rule and applies the change as one step under a lock, so a customer's cancel and the restaurant's start cannot both
succeed (LIFE-001, LIFE-003). A refused request returns 409 with the order as it now is. Each change is appended to a history (LIFE-002). The restaurant side is simulated through the same path (LIFE-006).
The full walkthrough, the guarantees and their limits are in [LIFECYCLE.md](LIFECYCLE.md). **Changing a placed order is not built;** the reasons and the questions to settle first are in [FUTURE-WORK.md](FUTURE-WORK.md).

## 6. Correctness: edge cases anticipated

Handled and tested: a cancel and a restaurant start at the same moment; a retry of a submission after a timeout, including simultaneous duplicates; a key reused for a different order; half-cent rounding; `SAVE5` at exactly $20.00 (not applied) and just above; discount larger than subtotal; promo
codes in any case and with spaces; unknown promo codes; two promotions (impossible by shape); duplicate toppings in any case; unknown
ids; quantity 0, negative and over the limit; `null` items and toppings; tip 0%, 100%, negative, over 100%, custom $0 and over the
limit; the tip not changing tax; the order changing while a price is in flight; the last pizza being removed; a failed price
lookup; a dropped connection on submit.

Found by probing the running code while writing these notes: **an order with 20,000 pizza lines was accepted**, because only the per-line
quantity was limited. It is now capped at 50 lines (PRC-014), and the UI stops offering more (UI-013).

Known and accepted: a 1 MB promo code is accepted and echoed back as "not recognized". It cannot change the price, and a length limit
would be a small follow-up. A custom tip with more than two decimals is rounded, not rejected (assumption 11).

## 7. Testing

**Which invariants, and why.** The registry has 46 invariants: 45 enforced and 1 planned. They are chosen where a mistake costs money or
trust, not where the code is simple. The pricing set (PRC) covers every term of the total and its arithmetic; the order set (ORD)
covers acceptance; the UI set covers the ways a live total can lie: arithmetic done in the browser, a stale number shown as current,
a slow response overwriting a newer one, a swallowed error, a wrong state after removing the last item.

**Beyond the happy path.**
- *Independent oracle:* generated orders compared with integer-cent arithmetic.
- *Boundaries:* exactly $20.00, half-cent tax, tip 0 and 100, quantity 0, 1, 10 and 11, 50 and 51 lines.
- *Race conditions:* out-of-order and aborted responses, debouncing.
- *Fixtures that would expose a wrong implementation:* UI tests use a fake server with deliberately unrealistic prices, so a UI that
  computed its own would show a different number.
- *Mutation checks:* each important test was shown to fail when the code was broken on purpose. This caught a missing assertion
  (UI-001 never checked the tax amount) and a real bug in my first version of the pricing hook (a late response from an aborted request
  could still apply).
- *Traceability:* `scripts/check-invariants.mjs` fails CI if an enforced invariant has no test or a test cites an unknown id.

**Counts:** 77 frontend tests; 77 backend tests (unit tests, and both back ends started as real HTTP servers).

**Concurrency tests that can fail.** Timing-based race tests are unreliable: on a one-core machine they kept passing even with the lock removed
(found by removing it). The decisive tests therefore freeze one request half-way through a change, using a test clock that pauses inside the store's
critical section, and check that the competing request must wait and is then refused. Removing the lock from `Transition` or `Create` now fails
exactly those tests. Randomised and multi-threaded tests remain as extra coverage.

**What was and was not run** when this was written: the frontend suite, lint, typecheck and production build ran. The backend unit and integration tests ran against the real code,
using a minimal stand-in for xunit and a stand-in that starts each real app on a local port (NuGet was unreachable), so the stock xunit runner and `WebApplicationFactory` have **not** run. The builder API,
the order service and the page were run together as three separate processes and driven through a browser and by `scripts/demo-order-service.sh`. Docker Compose and GitHub Actions did **not** run. There is no automated end-to-end test in the repo.

## 8. Extensibility

**Needs no change to pricing code** (proved by PRC-015 tests, including `A_new_size_crust_topping_and_promotion_work_from_menu_data_alone`):
- a new size, crust or topping, including a new category and rate;
- a new promotion of an existing type (`Percentage` or `FixedAmount`), with or without a minimum subtotal;
- a change to a price, a rate, a threshold or the tax rate.

**Needs a small code change:** a new *kind* of promotion (for example buy-one-get-one) means one new `case` in `EvaluatePromotion`. A
general rule engine was rejected as more than two promotion types need (ADR-0005).

**Where the data lives:** the menu is compiled into the API, so a menu change is a code edit and a deploy, though not a change to
*core logic*. Loading it from a JSON file is a small step that would remove the deploy, and it is the next extensibility improvement.

**The UI follows the menu.** The builder renders whatever the menu returns. Emoji and descriptive text for known items are decoration
keyed by id, and unknown items fall back to a default, so a new topping appears with no frontend change.

## 9. Judgment

**Cut on purpose:** authentication, accounts, payments, kitchen and delivery integration, menu administration, multiple locations,
order events and an outbox, cloud deployment work.

**Libraries.** React, Vite and TypeScript were already chosen. Added: Vitest, Testing Library and jsdom (the standard test stack for
Vite), and ESLint with the TypeScript and hooks plugins. Not added: a state library (a reducer is enough), a data-fetching library
(the needed rules are small and are written down in ADR-0003), a UI kit, a mocking library, or a property-testing library (a seeded corpus
is deterministic and has no new dependency). All versions are pinned exactly.

**Proportion.** For a three-hour exercise, six decision records and 37 invariants are more process than the code strictly needs. They
are kept because they are short, the invariant check is automatic, and they carry the reasoning forward. If time is tight, keep the registry,
the pricing tests and ADR-0001, and drop the rest.

**Pre-existing scaffolding that is out of proportion:** PostgreSQL, RabbitMQ and the worker are provisioned but unused, and the README
roadmap plans an outbox and event consumers. Both conflict with the scope above. They were marked out of scope in the README, not removed.

**Commit history.** Suggested sequence: (1) menu injection and promotion rules as data, with backend tests; (2) pricing invariant tests;
(3) frontend tooling; (4) cart domain and pricing hook with tests; (5) components and UI; (6) invariant registry, ADRs, agent files, CI and
scripts. Each step builds on its own.

## 10. Known gaps, in priority order

The decisions behind most of these, and what each would change, are in [ADR-0010](decisions/ADR-0010-decisions-outside-the-scope.md).

1. **Persistence.** Orders and idempotency keys disappear on restart, so a retry that spans a restart could create a duplicate. SQLite or the existing PostgreSQL, with a unique constraint on the key and a row version.
2. **More than one service instance.** The lock is in-process; a shared database and transactions would be needed (ADR-0007).
3. **The restaurant routes have no authentication,** and the restaurant has no screen: it is `scripts/restaurant.sh`, the endpoint, or the optional timer.
4. **Menu drift between the builder and the service** if they are deployed from different builds. A menu version stamp would detect it.
5. **Polling instead of push** for status (up to 5 seconds behind).
6. **Remove unused infrastructure** (RabbitMQ, worker) from the default stack, or build the integration they were meant for.
7. **Run the pieces not run here:** the stock xunit runner, `WebApplicationFactory`, Compose and CI.
8. **One error shape across every endpoint** (API-002, planned), and an **automated end-to-end test** of build, order, retry and cancel.
9. **Changing a placed order,** if the business case holds. See [FUTURE-WORK.md](FUTURE-WORK.md).
10. Upgrade ESLint (version 9 is no longer supported).
