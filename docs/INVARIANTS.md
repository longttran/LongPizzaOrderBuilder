# Invariants

An **invariant** is a statement about the system that must be true in every state and for every input, no matter
how the code changes. Invariants are the contract that tests exist to protect. They are written here once, given a
stable ID, and every test that protects one carries that ID, so the link between *rule* and *test* is checkable by
machine (`node scripts/check-invariants.mjs`, also run in CI).

## How this file works

- Each invariant is a `###` heading `ID · Title`, followed by a `**Status:**` line. The checker parses exactly that shape.
- **Status `enforced`** means at least one automated test references the ID. CI fails if none does.
- **Status `planned`** means agreed but not yet built. No test is required yet. CI fails the other way too: once a test
  references a planned ID, change its status to `enforced` so the registry stays truthful.
- Tests reference IDs in code: `[Trait("Invariant", "PRC-004")]` in C#, and `describe('UI-002: …')` / `it('PRC-004 …')` in
  TypeScript. Any ID used in a test must exist here, which catches typos.
- To add one: pick the next free number in its prefix, write the statement so it could be *false* (a good invariant is
  falsifiable), add the status, then write the test and put the ID on it. See `docs/decisions/ADR-0004`.

Prefixes: **PRC** pricing engine, **ORD** order acceptance, **API** HTTP contract, **UI** frontend behaviour,
**LIFE** order lifecycle. (Events were dropped from scope; see ADR-0006.) ORD, LIFE and API now describe the **order service**; see ADR-0009.

**Withdrawn IDs:** ORD-005, ORD-006 and UI-018 belonged to changing a placed order, which was deferred (see [FUTURE-WORK.md](FUTURE-WORK.md)). They are not reused. If that work is built, its invariants are drafted in FUTURE-WORK.md.

All money rules use the agreed domain facts: sizes Small $5 / Medium $7 / Large $12; basic toppings 8% and premium
toppings 11% of the pizza's base price; tax 6.5%; rounding half away from zero to 2 decimals.

---

## Pricing engine (server)

### PRC-001 · Base prices
**Status:** enforced
A pizza with no toppings costs exactly the size's base price: Small $5.00, Medium $7.00, Large $12.00. Crust never changes the price.

### PRC-002 · Topping price is a percentage of the pizza's base price
**Status:** enforced
Each topping costs its rate × the base price of the pizza it is on (never of the running subtotal), rounded to cents.

### PRC-003 · Line total
**Status:** enforced
A line's unit price is the base price plus its toppings, and its line total is unit price × quantity.

### PRC-004 · Subtotal
**Status:** enforced
The subtotal is the sum of the line totals, with nothing else added.

### PRC-005 · SAVE5 threshold
**Status:** enforced
SAVE5 takes $5.00 off only when the pre-discount subtotal is strictly greater than $20.00. At exactly $20.00 it does not apply.

### PRC-006 · ILOVEPIZZA
**Status:** enforced
ILOVEPIZZA takes 10% off the pre-tax subtotal, and the discount is applied before tax.

### PRC-007 · At most one promotion, and a bad code costs nothing
**Status:** enforced
One order uses at most one promotion. An unknown or ineligible code never reduces the total, and `applied` is true exactly when a discount is given. Codes are matched ignoring case and surrounding spaces.

### PRC-008 · Discount bounds
**Status:** enforced
0 ≤ discount ≤ subtotal. A discount can never make the discounted subtotal negative.

### PRC-009 · Tax base
**Status:** enforced
Tax is 6.5% of the discounted subtotal. The tip is never taxed, so changing the tip never changes the tax.

### PRC-010 · Tip
**Status:** enforced
No tip is $0.00. A percentage tip is that percentage of (discounted subtotal + tax). A custom tip is the amount entered (rounded to cents if more decimals are typed) and is neither taxed nor discounted.

### PRC-011 · Total reconciles exactly
**Status:** enforced
total = discounted subtotal + tax + tip, and discounted subtotal = subtotal − discount, with no unexplained cents.

### PRC-012 · Rounding
**Status:** enforced
Every money amount has at most two decimal places, and half-cent values round away from zero (for example tax on $5.00 is $0.33, not $0.32).

### PRC-013 · Pricing is a pure function
**Status:** enforced
The same request always gives the same breakdown, and pricing never modifies the request.

### PRC-014 · Invalid input is rejected, not guessed at
**Status:** enforced
An unknown size or crust, an unknown or duplicated topping, a quantity outside 1–10, more than 50 pizza lines, a bad tip, or an empty order yields errors and no breakdown. An invalid order is never discounted.

### PRC-015 · Pricing is driven by menu data
**Status:** enforced
Prices, topping rates, the tax rate and promotion rules (including thresholds) are read from the menu. Replacing the menu changes prices with no code change.

## Order service (a separate process the order builder talks to over HTTP)

The customer can send an order and get an ID, query its status, and update or cancel it, but only while it is **NotStarted**.
The restaurant side advances the status. Statuses: **NotStarted → Started → ReadyForPickup → Delivered**, plus **Cancelled**
(a terminal state for an order the customer cancelled; the brief lists four statuses, and a cancelled order needs an honest status of its own).
Customers are on unreliable connections and will retry, so every customer action must be safe to repeat.

### ORD-001 · No empty or invalid orders
**Status:** enforced
The service never accepts an order with no valid pizza, a blank name, or a name over 80 characters.

### ORD-002 · The service prices every order itself
**Status:** enforced
The service calculates the price with the same pricing code the order builder previews with, stores that price, and ignores any amount the client sends. For the same choices the builder's preview and the service's accepted total are identical.

### ORD-003 · The accepted items and price never change
**Status:** enforced
An order's items and accepted price are fixed when it is placed. Status changes, refused requests and repeated requests never change them. (Changing a placed order is not built; see docs/FUTURE-WORK.md.)

### ORD-004 · Sending an order is safe to repeat
**Status:** enforced
An `Idempotency-Key` is required. Sending the same key again returns the original order (with its current status) instead of creating another, even when the repeats arrive at the same moment. The same key with a different order is refused. A request that failed validation does not use up its key.

### LIFE-001 · Legal status moves only
**Status:** enforced
The restaurant moves an order NotStarted → Started → ReadyForPickup → Delivered, one step at a time. Only the customer can cancel, and only from NotStarted. Delivered and Cancelled are final. A move that is not allowed is refused and changes nothing, not even the history.

### LIFE-002 · The history is a faithful record
**Status:** enforced
Every accepted change (placed, each status move, cancelled) is recorded in order with its time and who made it. The current status equals the last event's status, every recorded move was legal, and the customer appears only as the one who placed or cancelled the order.

### LIFE-003 · A customer's cancel and the restaurant starting cannot both win
**Status:** enforced
Requests to change the same order are decided one after the other. If the customer cancels while the restaurant starts the order, exactly one of the two is applied and the other is refused with the order's true status.

### LIFE-004 · The customer can cancel only before the restaurant starts
**Status:** enforced
The customer can cancel only while the order is NotStarted. The restaurant can never cancel. The service reports whether the customer can still cancel, so the browser does not have to copy the rule.

### LIFE-005 · Repeating a cancel or a restaurant move is harmless
**Status:** enforced
Cancelling an already cancelled order, or asking for the status an order already has, succeeds without changing anything and records nothing new.

### LIFE-006 · The restaurant simulation moves orders safely
**Status:** enforced
When the timer simulation is on, an order moves one step only after it has waited the configured time in its current status. It never skips a step, never moves a Delivered or Cancelled order, and never makes a move the customer could have beaten (it uses the same checked path as every other move).

### API-001 · Client-supplied money is ignored
**Status:** enforced
Price, tax, total or discount fields in a request body have no effect on the result.

### API-002 · One error shape
**Status:** planned
Every rejected request returns the same RFC 7807 problem shape, with a machine-readable list of field errors.

### API-003 · Customers and the restaurant have separate doors
**Status:** enforced
The customer API can send, query and cancel an order. It cannot list orders or move an order along. Listing and moving orders exist only on the restaurant API.

### API-004 · A required header is enforced
**Status:** enforced
Creating an order without an `Idempotency-Key`, or with a malformed one, is refused with a message saying what is wrong.

## Frontend

### UI-001 · No money arithmetic in the browser
**Status:** enforced
Every subtotal, discount, tax, tip, line total and total on screen is a value the server returned. The browser formats amounts but never adds, multiplies or rounds them. The only derived number is a per-topping price hint on the topping cards, which is never summed.

### UI-002 · A stale price is never shown as current
**Status:** enforced
A price is presented as current only if it was calculated for exactly the order on screen. Otherwise the total is marked as updating, a pizza whose choices changed shows a placeholder, and ordering is disabled until the new price arrives.

### UI-003 · A slow reply cannot overwrite a newer one
**Status:** enforced
If pricing replies arrive out of order, or a superseded request resolves late, the newest order's price wins.

### UI-004 · An empty order is zero and cannot be submitted
**Status:** enforced
With no pizzas the total is $0.00, checkout is disabled, no pricing request is sent, and order details are not offered.

### UI-005 · Removing a pizza removes its whole contribution
**Status:** enforced
After a pizza is removed it no longer appears in the order or in the price, and the selected pizza is always valid or none.

### UI-006 · Failures are shown, not swallowed
**Status:** enforced
Validation errors from the server, an unreachable pricing service and an unloadable menu each produce a visible message. No total is shown when none could be calculated.

### UI-007 · The promotion verdict is the server's
**Status:** enforced
The entered code stays visible, the server's message explains whether it applied, and the verdict is re-checked after every change to the order.

### UI-008 · Order details are on request and harmless
**Status:** enforced
Details appear only when asked for, behave as a modal dialog (focus moves in, Escape closes, focus returns), and opening or closing them changes nothing about the order.

### UI-009 · Submission sends choices, never prices
**Status:** enforced
The order request contains sizes, crusts, toppings, quantities, promo code and tip choice only. Ordering is allowed only when the current order has a valid server price and a name.

### UI-010 · Tip choices map to the API faithfully
**Status:** enforced
No tip, a percentage and a custom amount map to the matching API shape. A custom amount stays fixed as the order changes, and a negative value is passed on so the server can reject it.

### UI-011 · An unknown outcome is not reported as failure
**Status:** enforced
If the connection drops or times out while placing an order, the customer is told it may or may not have been received, not that it failed, and is told that pressing the button again is safe. A clear rejection shows the server's reason.

### UI-012 · The total follows every change
**Status:** enforced
Changing size, crust, toppings or quantity of the pizza being built updates the running total. Edits apply to the selected pizza immediately.

### UI-013 · Each topping once; quantity within bounds
**Status:** enforced
A topping can be on a pizza at most once, quantity stays between 1 and the maximum, and the order never grows past the maximum number of pizza lines.

### UI-014 · Pickers work by keyboard and expose their state
**Status:** enforced
Size and crust are radio groups with arrow-key navigation, and toppings expose pressed state.

### UI-015 · Pressing Place order again cannot create a second order
**Status:** enforced
Each checkout attempt has one unguessable key. A retry of the same order after a timeout or server error reuses it. Changing the cart or name, a clear refusal (4xx) or a success starts a new key. A second press is blocked while a request is in flight.

### UI-016 · Status comes from the service, and cancelling tells the truth
**Status:** enforced
The order's status is shown in words from the service's last answer. The Cancel button appears only when the service says the customer can still cancel. A cancel that succeeds shows Cancelled. If the restaurant got there first, the customer sees the service's reason and the real status, never a false success. If the cancel cannot be confirmed, the customer is told so, the status is re-checked, and pressing again is safe.

### UI-017 · The status follows the restaurant and never goes backwards
**Status:** enforced
The status refreshes until the order is Delivered or Cancelled. A snapshot with a shorter history cannot replace a newer one, and a failed refresh keeps the last known status and says it could not refresh.
