# LongPizzaOrderBuilder

Build a pizza order in a web page and watch the total update, then send it to a separate **order service** that holds the order and its
lifecycle. One restaurant, anonymous customers. Deliberately not built: authentication or accounts, real payments, kitchen or delivery
integration, a menu admin UI, multiple locations.

| Process | What it does | Port |
|---|---|---|
| Order builder page (`src/frontend`) | React. Builds the order, shows the live total, sends it, shows its status | 5173 |
| Order builder API (`…Api`) | The menu and a stateless price preview | 8080 |
| **Order service** (`…OrderService`) | A separate process. Holds orders, the lifecycle, and the restaurant's side | 8090 |
| Pricing library (`…Pricing`) | The one place money is calculated. Used by both back ends | n/a |

---

# Part 1: Run it, call it, test it

## Run both locally

```bash
docker compose up --build      # builder page, builder API and order service; open http://localhost:5173
scripts/dev.sh                 # the same without Docker (needs .NET 10 SDK and Node 22)
```

Then play the restaurant. Orders start **Not Started**; the restaurant side moves them (needs bash and curl; Git Bash or WSL on Windows):

```bash
scripts/seed.sh                          # a few sample orders, one in every status
scripts/restaurant.sh list               # the restaurant's view of all orders
scripts/restaurant.sh start <orderId>    # then:  ready <orderId>,  deliver <orderId>
RESTAURANT_AUTO_ADVANCE_SECONDS=30 scripts/dev.sh   # or let a timer play the restaurant (30 s per step). Off by default here; on in compose.selfhost.yaml, which does not publish the restaurant routes
scripts/demo-order-service.sh            # a retry after a timeout, and cancel-versus-start, with the output
```

### Starting a pending order (testing the restaurant side)

1. Place an order in the page. It shows **Not Started** and an order number such as `#B44C98FB`. That number is only the **first 8 characters** of the order's id, and the commands need the **full 32-character id**; the short one gives `404 Order not found`.
2. Get the full id: `scripts/restaurant.sh list NotStarted` prints `<full id>  <status>  <customer name>`, so match on the name you typed.
3. Start it: `scripts/restaurant.sh start <full id>`, then `ready <full id>`, then `deliver <full id>`. The restaurant moves one step at a time, so skipping a step is a 409.
4. Watch the page: it checks every **5 seconds**, so allow up to 5 seconds for "Started" to appear. To see cancel-versus-start, start the order and press **Cancel this order** before the page refreshes. The cancel is refused with "The restaurant has already started this order…" and the page shows the real status.

Without bash, the same calls work from PowerShell (these were not run when this was written; the equivalent `curl` calls were):

```powershell
Invoke-RestMethod "http://localhost:8090/api/restaurant/orders?status=NotStarted" | Select-Object id, customerName, status
Invoke-RestMethod -Method Post -Uri "http://localhost:8090/api/restaurant/orders/<full id>/transitions" -ContentType "application/json" -Body '{"to":"Started"}'
```

## Run the tests

```bash
dotnet test LongPizzaOrderBuilder.slnx                   # backend: unit tests, and both services over real HTTP
cd src/frontend && npm ci && npm test                    # frontend (also: npm run lint, npm run build)
node scripts/check-invariants.mjs                        # every invariant marked "enforced" has a test that names it
```

`scripts/verify.sh` runs all of it. *Verification note:* the backend and frontend suites, the three processes together, and the demo scripts
were run when this was written; Docker Compose, GitHub Actions and the stock xunit runner were not (the backend tests were run through a minimal
stand-in because NuGet was unreachable), so please run the commands above once.

## The order service API (`http://localhost:8090`)

**Statuses:** `NotStarted` ("Not Started") → `Started` → `ReadyForPickup` ("Ready for Pickup") → `Delivered`. A customer can **cancel only
while `NotStarted`**. A cancelled order shows `Cancelled` (an extra, final status: the order has to say what happened to it). Changing a placed order is **not built**; see [`docs/FUTURE-WORK.md`](docs/FUTURE-WORK.md).

| Customer | | |
|---|---|---|
| `POST /api/orders` | Send an order, get its id. **Requires `Idempotency-Key`** | 201 new · 200 repeat · 422 key reused for a different order |
| `GET /api/orders/{id}` | Status and history | 200 · 404 |
| `POST /api/orders/{id}/cancel` | Cancel | 200 (also if already cancelled) · 409 too late |

| Restaurant (no authentication: keep it off the public internet) | | |
|---|---|---|
| `GET /api/restaurant/orders?status=NotStarted` | See orders, optionally one status | 200 |
| `POST /api/restaurant/orders/{id}/transitions` `{"to":"Started"}` | Move one step: Started, then ReadyForPickup, then Delivered | 200 · 409 not allowed |

Errors are `{"errors":["…"]}`; a 409 also carries `"order"`, the order as it is now. Prices are always calculated by the service.

```bash
curl -i -X POST localhost:8090/api/orders -H 'Content-Type: application/json' -H 'Idempotency-Key: 5d1f6c9e-0b8a-4e7a-9b1d-2c3a4f5e6d7c' -d '{
  "customerName": "Sam",
  "items": [{ "size": "Large", "crust": "Thin", "toppings": ["pepperoni", "bacon"], "quantity": 2 }],
  "promoCode": "SAVE5", "tip": { "type": "Percent", "percent": 18 } }'
# HTTP/1.1 201 Created     Location: /api/orders/bd776a08…
# { "id": "bd776a0863e148569941aa0a396ec694", "status": "NotStarted", "canCustomerCancel": true,
#   "pricing": { "subtotal": 28.56, "discount": 5.0, "tax": 1.53, "tip": 4.52, "total": 29.61, … },
#   "history": [{ "event": "Placed", "status": "NotStarted", "actor": "Customer", … }] }

curl localhost:8090/api/orders/$ID                                              # status
curl -X POST localhost:8090/api/orders/$ID/cancel
curl -X POST localhost:8090/api/restaurant/orders/$ID/transitions -H 'Content-Type: application/json' -d '{"to":"Started"}'
# after that: cancel returns 409 "The restaurant has already started this order…" with the order's true status
```

The builder API (`:8080`) offers `GET /api/menu` and `POST /api/pricing/preview` only. It has no order endpoints.

## Invariants: what is tested, why, and what is not

The registry is [`docs/INVARIANTS.md`](docs/INVARIANTS.md) (46 rules; 45 enforced; each enforced rule is named in at least one test, and a script checks that).
I chose the ones where a mistake costs money, makes a duplicate or lost order, or tells a customer something false:

| Invariant | Why it was chosen | How it is tested |
|---|---|---|
| Money is right to the cent (PRC-001 to 015) | A wrong total is the first failure anyone notices | About 20,000 generated orders against a second, independent integer-cents calculation |
| The service decides the price, and the builder agrees (ORD-002) | Trusting a browser's total would be the worst bug | Builder preview and service charge compared over HTTP; client money fields ignored |
| Sending twice makes one order (ORD-004) | Mobile networks time out and customers retry | Same key twice, key reused, 64 simultaneous duplicates, a retry that arrives mid-save |
| Only legal status moves; the history is true (LIFE-001, 002) | The lifecycle is the product | Every from/to pair against a hand-written table; random sequences |
| Cancel only before the restaurant starts, and one winner in a race (LIFE-003, 004) | The exact situation of cancelling as the kitchen starts | One request is frozen half-way through its change while the other is sent; removing the lock fails these |
| Repeating a cancel or a restaurant move is harmless (LIFE-005) | Requests arrive twice | Repeated cancels and restaurant moves change nothing and record nothing |
| The screen never shows a stale price or status as current, and never says "failed" when the outcome is unknown (UI-001 to 003, 011, 015 to 017) | What the customer sees is what they act on | Fake service with deliberately unrealistic prices; delayed, dropped and out-of-order replies |

**Not covered:** changing a placed order (deferred on purpose; see [`docs/FUTURE-WORK.md`](docs/FUTURE-WORK.md)); one error shape across every endpoint (API-002, planned); anything that survives a restart (orders and keys are in memory);
more than one service instance; real network faults and load (a dropped reply was simulated once in a browser, not as an automated test);
authentication of the restaurant routes (none, by scope); drift between the builder and the service if they are deployed from different
builds (only same-build agreement is tested); accessibility beyond keyboard use and labels; an automated end-to-end suite.

---

# Part 2: Design, in one page

## 1. Money, the total, and where pricing lives

**Money** is C# `decimal`, never a float, and reaches the browser as JSON numbers that it only displays. The total is built in a fixed order, rounding
half away from zero to cents at every step so the parts shown always add up: each topping is its rate × the *pizza's* base price → line total = unit × quantity → subtotal →
one promotion (never more than the subtotal) → tax 6.5% of what remains → tip (a percentage of subtotal after discount plus tax, or a fixed amount) → total.

**Where:** all of it is in one library, `LongPizzaOrderBuilder.Pricing`, used by **both** back ends and by no front end. The builder API uses it for the
live preview, which is stateless and sits next to the menu the page already loads. The order service uses it to price every order, because it is the one that
issues the receipt and so must never trust a total from a phone or from the builder. Sharing the code, not copying it, means the two cannot disagree (a test checks they do not).
The cost: if the two were deployed from different builds, their menus could drift; a menu version stamp is the fix (see section 4).

## 2. Adding a size, topping category, or promo code

A new size, crust, topping (with its category and rate) or tax rate is **a row of data** in `PizzaMenu`; the pricing code does not change, and the page renders whatever the menu
returns (a test adds a whole new size, crust, topping and promo using nothing but data). A new promo code is a `PromotionDescription` row: a code, a type (`Percentage` or `FixedAmount`),
a rate or amount, and an optional minimum subtotal, which is its own condition (for example SAVE5 applies only above $20.00).

*Conditions and conflicts, honestly:* today an order carries **one** code, so promos cannot conflict by construction, and the only condition is the minimum subtotal. A promo with a richer condition
(only with a Large pizza, only on weekdays) adds an optional field to its row and one check in `EvaluatePromotion`. Stacking is **designed, not built**: promos gain `ConflictsWith` and a priority, the
request carries a list of codes, and the engine applies them in priority order, skipping any that conflicts with one already applied and saying why (as it already does for "not applied: reason").
The existing rules (a discount never exceeds the subtotal; a bad code costs nothing) would carry over unchanged.

## 3. The order lifecycle, a retry, and a cancel at the moment the kitchen starts

`NotStarted → Started → ReadyForPickup → Delivered`, or `NotStarted → Cancelled`. One table defines the legal moves; the only way to change an order is the store's `Transition`, which
checks the rule and applies the change under one lock, as one step. Full walkthrough with real output: [`docs/LIFECYCLE.md`](docs/LIFECYCLE.md).

**A retry after a timeout.** The page makes one random key per order and sends it with the order. If the reply is lost, the page says the order "may or may not have been received" (not "failed") and,
when the customer presses the button again, resends the *same* request and key. The service finds the key and returns the original order (200, `Idempotency-Replayed: true`); nothing new is created.
The same key with a different order is refused (422).

**Cancel just as the restaurant starts.** Both requests reach the store and are decided one after the other; exactly one succeeds. If the cancel is first the order is `Cancelled` and the restaurant's start is refused
(409). If the start is first the order is `Started` and the cancel is refused (409, "the restaurant has already started this order") with the order as it now is, which the page shows. There is no third outcome.

**Guarantees, and how:** one order per key, even for simultaneous duplicates (key checked and order saved inside the lock); only legal moves, and a refused move changes nothing (one rule table); one winner in a race (the lock,
proved by freezing a request mid-change); the customer is told the truth (a 409 carries the current order); a repeat is harmless (asking for the current state succeeds
and records nothing); the history is a faithful record (appended only on a real change). **Not guaranteed:** surviving a restart, or running more than one instance (the lock is in-process), or protecting the restaurant routes (no authentication).

## 4. What was cut or faked, and what comes next

**Cut or faked to stay in the timebox:** **changing a placed order** (a customer can cancel, or cancel and place again; a full version was prototyped and removed because it needs a design and a business discussion first, see [`docs/FUTURE-WORK.md`](docs/FUTURE-WORK.md)); orders and idempotency keys live in memory and vanish on restart; the restaurant is a script, an endpoint and an optional timer, with no screen and no login; the page learns of status changes by polling every 5 seconds, not by push;
promos are one-per-order; no payments (a tip is just a number); the restaurant cannot cancel; no estimated time; the builder and service share a menu by sharing a build, with no version check; Docker Compose and CI were not exercised here.

**Next, in order:** (1) persistence (SQLite or the PostgreSQL already in Compose) with a unique constraint on the key, which also makes duplicates safe across restarts and several instances;
(2) authentication for the restaurant routes; (3) a restaurant screen and push updates instead of polling; (4) a menu version stamped on every price, so the builder and service can detect drift; (5) promo stacking rules as described above;
(6) one error shape across all endpoints (API-002); (7) changing a placed order, if the business case in [`docs/FUTURE-WORK.md`](docs/FUTURE-WORK.md) holds. Decisions that sit outside this scope but would have helped are written up in [`docs/decisions/ADR-0010`](docs/decisions/ADR-0010-decisions-outside-the-scope.md).

---

More: [design notes and assumptions](docs/DESIGN.md) · [lifecycle](docs/LIFECYCLE.md) · [invariants](docs/INVARIANTS.md) · [decisions](docs/decisions/README.md) ·
[setup and deployment details](docs/OPERATIONS.md) · [original planning notes](docs/PLANNING.md) · [project history](docs/HISTORY.md) · [future work](docs/FUTURE-WORK.md) · [guidance for AI agents](AGENTS.md)
