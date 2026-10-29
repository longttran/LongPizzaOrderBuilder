# Product goals and business rules

> Pruned 2026-10-28. This page keeps what is still true: the goals, the business rules the code implements, and the intended behavior of the order builder page.
> The rest of the early planning notes (a status table, an endpoint list, a domain-model sketch, an architecture table, a roadmap, a lifecycle design, 14 invariants, a test checklist and some open questions) described an earlier, single-process design and have been replaced by the current documents.
> What each one said, and where the current truth lives, is in [Previous iterations](#previous-iterations) at the end. The full earlier text is in Git history (`git log --follow docs/BUSINESS-RULES.md`).

## Project goals

LongPizzaOrderBuilder should let a customer configure one or more pizzas, understand the price as choices change, apply promotions, choose a tip, and submit an order. The same business rules should be enforced by the C# backend and presented clearly by the React UI.

Design principles:

1. **The server is authoritative for price.** The browser sends choices, not trusted totals or discounts.
2. **Menu options should be data-driven.** Adding a size or topping should generally be a menu/configuration change rather than a code change.
3. **Preview and checkout should share the same pricing engine.** A preview is an estimate; order submission recalculates and returns the authoritative result. (Today both back ends call one shared library; persisting the result comes with a database, see [FUTURE-WORK.md](FUTURE-WORK.md).)
4. **Order receipts must be stable.** An accepted order keeps its price, discount, tax, and total, so a later menu change does not rewrite it (invariant ORD-003; held in memory until persistence is built).
5. **Use REST for immediate request/response and events for asynchronous work.** Do not introduce distributed services without a concrete need. (The order service is a second service because the brief asks for one; events are not built, see [ADR-0010](decisions/ADR-0010-decisions-outside-the-scope.md).)
6. **Keep local and cloud environments container-friendly.** Use Docker Compose locally and deploy the same built images to a chosen cloud container platform.

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

- **Tax:** 6.5% of the order subtotal after the promotion discount and before tip. The tip is excluded from the taxable subtotal.
- **Tip:** customers may select no tip, a percentage tip, or a custom dollar amount. A percentage tip is calculated on the post-discount, post-tax amount. A custom dollar tip is added to the order total without being discounted or taxed.
- **`ILOVEPIZZA`:** applies a 10% discount to the eligible order subtotal before tax.
- **`SAVE5`:** subtracts $5 when the eligible subtotal before discount and tax is strictly greater than $20. An order subtotal of exactly $20 does not qualify.
- **Promotion stacking:** ILOVEPIZZA and SAVE5 cannot be combined. Only one promotion may be applied to an order.
- **Invalid promotions:** unrecognized or ineligible codes do not reduce the total. The customer receives a clear explanation.
- **Rounding:** use C# decimal for monetary calculations and MidpointRounding.AwayFromZero to round monetary amounts to two decimal places.

### Tip calculation decision
The requirements allow a tip to be entered as either a dollar amount or a percentage of the "total bill," but do not explicitly define the percentage-tip calculation base or the order of operations. We therefore adopt the following explicit policy: **percentage tips are calculated after applying the promotion and calculating tax**.

This policy is a documented implementation decision rather than a rule directly stipulated by the exercise. It gives percentage tips a predictable base that includes the discounted food amount and tax, while excluding the tip itself from both discounts and tax. A custom dollar tip is added as entered.

### Calculation order

1. Calculate each pizza's base price and topping charges, multiplied by quantity.
2. Sum all line totals to produce the pre-discount subtotal.
3. Validate and apply at most one eligible promotion.
4. Calculate tax at 6.5% on the discounted subtotal.
5. Calculate a percentage tip from the discounted subtotal plus tax, or use the custom dollar tip.
6. Calculate the final total as discounted subtotal + tax + tip.

The pricing engine is the single source of truth for both the live pricing preview and final order submission. The server validates menu selections and recalculates all monetary values; client-supplied prices and totals are never authoritative.

## Order builder behavior

This is the intended behavior of the order builder page. **Built:** several line items, quantity limits, a running total that is never stale, the empty-order state, the three tip modes, promotion messages, and the on-demand order-details dialog. **Not built:** merging identical lines, a best-value promotion recommendation, and the two-code conflict message (an order carries one code, so two codes cannot conflict). The rules that protect this behavior are in [INVARIANTS.md](INVARIANTS.md).

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

## Previous iterations

### What was removed, and what replaced it

| Early planning note | What it said | Where the current truth is |
|---|---|---|
| Implementation status table | The first prototype: orders held in one API, PostgreSQL, RabbitMQ and a worker provisioned but unused, CI unverified | [README](../README.md) and [DESIGN.md](DESIGN.md) section 7 |
| Implemented API endpoints | Menu, preview and orders all in one API, including a `GET /api/orders` list and legacy single-pizza fields | The order service API in the [README](../README.md): orders are in a separate service, and the customer door cannot list orders |
| Domain model direction | A larger database-style model (`MenuProduct`, `CustomizationOption`, `Promotion`, and so on) with receipt snapshots | [DESIGN.md](DESIGN.md) section 3. The receipt idea survives as invariant ORD-003; persistence is in [FUTURE-WORK.md](FUTURE-WORK.md) |
| API contract direction | A planned `POST /api/promotions/validate`, and guidance for the page | Promotion checking is part of the price preview; the page guidance is built ([ADR-0003](decisions/ADR-0003-pricing-preview-semantics.md)) |
| Architecture decisions table | A modular monolith; PostgreSQL with EF Core; RabbitMQ with a transactional outbox; a shared pricing engine; receipt snapshots | [ADR-0001 to ADR-0010](decisions/README.md). Two points changed: a separate order service ([ADR-0009](decisions/ADR-0009-separate-order-service.md)), and PostgreSQL, RabbitMQ and the worker were removed because nothing used them ([ADR-0010](decisions/ADR-0010-decisions-outside-the-scope.md)) |
| Roadmap, P0 to P2 | P0 align with the business rules (done); P1 persistence, outbox and events; P2 production readiness | [FUTURE-WORK.md](FUTURE-WORK.md). Events, a kitchen, authentication and payments remain out of scope |
| Lifecycle UX | Draft, Submitted, InProgress, Delayed, Completed, Cancelled; an ETA; status history; integration events | [LIFECYCLE.md](LIFECYCLE.md): Not Started, Started, Ready for Pickup, Delivered, Cancelled. A delay and an ETA were not built (a delay would be a note on Started, not a status; [ADR-0007](decisions/ADR-0007-order-lifecycle-and-concurrency.md)) |
| 14 critical invariants | The first statement of the rules to protect | [INVARIANTS.md](INVARIANTS.md): 46 rules, each with tests |
| Testing requirements | A checklist of tests to write, with a 17-test baseline | The tests themselves; [INVARIANTS.md](INVARIANTS.md) maps each rule to them |

### Questions from the first iteration, and what happened

| Question | Outcome |
|---|---|
| Can a customer edit or cancel after submitting? | **Cancel, only while Not Started (confirmed).** Editing is deferred ([FUTURE-WORK.md](FUTURE-WORK.md), item 1). Refunds and payments are out of scope |
| How is an ETA estimated, and which delay reasons are safe to show? | Not built ([FUTURE-WORK.md](FUTURE-WORK.md), item 14) |
| Maximum quantity, custom-tip limit, duplicate toppings, extra portions? | Quantity 1 to 10, at most 50 lines, a custom tip from $0 to $10,000, a percentage tip from 0 to 100, each topping once per pizza, no extra portions ([DESIGN.md](DESIGN.md), assumptions 5 and 11) |
| Should a promotion recommendation suggest the best code or apply it? | Neither is built ([FUTURE-WORK.md](FUTURE-WORK.md), item 6, covers stacking and conflicts) |
| What if the menu or rules change between preview and submission? | The service reprices at submission, so the preview can be out of date. A menu version stamp is future work (item 5) |
