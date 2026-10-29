# LongPizzaOrderBuilder

Build a pizza order in a web page, watch the total update, then send it to a separate **order service** that holds the order and its lifecycle. One restaurant, anonymous customers.  Design was based on [design notes and assumptions](docs/DESIGN.md).
Not built, on purpose: authentication or accounts, real payments, kitchen or delivery integration, a menu admin UI, multiple locations.

| Process | Role | Port |
|---|---|---|
| Order builder page (`src/frontend`) | React: builds the order, shows the live total, sends it, shows its status | 5173 |
| Order builder API (`…Api`) | The menu and a stateless price preview | 8080 |
| **Order service** (`…OrderService`) | A separate process: orders, the lifecycle, and the restaurant's side | 8090 |
| Pricing library (`…Pricing`) | The one place money is calculated; used by both back ends | n/a |

---

# Part 1: Run it, call it, test it

## Run

```bash
docker compose up --build   # page :5173, builder API :8080, order service :8090; open http://localhost:5173
scripts/dev.sh              # the same without Docker (.NET 10 SDK and Node 22)
```

If you ran an older version of the stack, add `--remove-orphans` once. Orders start **Not Started**; the restaurant side moves them (bash and curl; Git Bash or WSL on Windows):

```bash
scripts/seed.sh                         # sample orders, one in every status
scripts/restaurant.sh list NotStarted   # <full id>  <status>  <customer name>
scripts/restaurant.sh start <full id>   # then: ready, deliver (one step at a time)
RESTAURANT_AUTO_ADVANCE_SECONDS=30 scripts/dev.sh   # or let a timer play the restaurant
scripts/demo-order-service.sh           # a retry after a timeout, and cancel-versus-start, with output
```

- The page's order number (for example `#B44C98FB`) is only the **first 8 characters** of the id. The commands need the **full id**; the short one gives `404`.
- The page checks every **5 seconds**. To see cancel-versus-start, start the order, then press **Cancel this order** before it refreshes: the cancel is refused with the reason.
- PowerShell (not run when written): `Invoke-RestMethod "http://localhost:8090/api/restaurant/orders?status=NotStarted"`, and `Invoke-RestMethod -Method Post -Uri "http://localhost:8090/api/restaurant/orders/<full id>/transitions" -ContentType application/json -Body '{"to":"Started"}'`.

## Test

```bash
dotnet test LongPizzaOrderBuilder.slnx        # backend: unit tests, and both services over real HTTP
cd src/frontend && npm ci && npm test         # frontend (also: npm run lint, npm run build)
node scripts/check-invariants.mjs             # every invariant marked "enforced" is named by a test
```

`scripts/verify.sh` (or `scripts/verify.ps1` in PowerShell) runs all of it. What has and has not been run: [`docs/DESIGN.md`](docs/DESIGN.md) section 7. The warnings `npm ci` prints are explained in [`docs/DEPENDENCY-NOTES.md`](docs/DEPENDENCY-NOTES.md).

## The order service API (`http://localhost:8090`)

**Statuses:** `NotStarted` ("Not Started") → `Started` → `ReadyForPickup` ("Ready for Pickup") → `Delivered`. **Confirmed rule:** a customer can **cancel only while `NotStarted`**.
A cancelled order shows `Cancelled`, an extra final status so the order can say what happened. Changing a placed order is not built ([`docs/FUTURE-WORK.md`](docs/FUTURE-WORK.md)).

| Customer | | |
|---|---|---|
| `POST /api/orders` | Send an order, get its id. **Requires `Idempotency-Key`** | 201 new · 200 repeat · 422 key reused for a different order |
| `GET /api/orders/{id}` | Status and history | 200 · 404 |
| `POST /api/orders/{id}/cancel` | Cancel | 200 (also if already cancelled) · 409 too late |

| Restaurant (no authentication: keep it off the public internet) | | |
|---|---|---|
| `GET /api/restaurant/orders?status=NotStarted` | See orders, optionally one status | 200 |
| `POST /api/restaurant/orders/{id}/transitions` `{"to":"Started"}` | Move one step: Started, ReadyForPickup, then Delivered | 200 · 409 not allowed |

Errors are `{"errors":["…"]}`; a 409 also carries `"order"`, the order as it is now. Prices are always calculated by the service. The builder API (`:8080`) has only `GET /api/menu` and `POST /api/pricing/preview`.

```bash
curl -i -X POST localhost:8090/api/orders -H 'Content-Type: application/json' -H 'Idempotency-Key: 5d1f6c9e-0b8a-4e7a-9b1d-2c3a4f5e6d7c' -d '{
  "customerName": "Sam",
  "items": [{ "size": "Large", "crust": "Thin", "toppings": ["pepperoni", "bacon"], "quantity": 2 }],
  "promoCode": "SAVE5", "tip": { "type": "Percent", "percent": 18 } }'
# 201 Created   { "id": "bd776a0863e148569941aa0a396ec694", "status": "NotStarted", "canCustomerCancel": true,
#                 "pricing": { "subtotal": 28.56, "discount": 5.0, "tax": 1.53, "tip": 4.52, "total": 29.61, … }, "history": [ … ] }

curl localhost:8090/api/orders/$ID                       # status
curl -X POST localhost:8090/api/orders/$ID/cancel        # after the restaurant starts: 409 "The restaurant has already started this order…"
curl -X POST localhost:8090/api/restaurant/orders/$ID/transitions -H 'Content-Type: application/json' -d '{"to":"Started"}'
```

## Invariants: what is tested, why, and what is not

The registry is [`docs/INVARIANTS.md`](docs/INVARIANTS.md): 46 rules, 45 enforced, each named in at least one test (a script checks that). I chose the ones where a mistake costs money, duplicates or loses an order, or tells a customer something false:

| Invariant | Why | How it is tested |
|---|---|---|
| Money is right to the cent (PRC-001 to 015) | A wrong total is the first failure anyone notices | About 20,000 generated orders against a second, independent integer-cents calculation |
| The service decides the price, and the builder agrees (ORD-002) | Trusting a browser's total would be the worst bug | Builder preview and service charge compared over HTTP; client money fields ignored |
| Sending twice makes one order (ORD-004) | Mobile networks time out and customers retry | Same key twice, key reused, 64 simultaneous duplicates, a retry that arrives mid-save |
| Only legal status moves; the history is true; repeats are harmless (LIFE-001, 002, 005) | The lifecycle is the product | Every from/to pair against a hand-written table; random sequences; repeated cancels and moves |
| Cancel only before the restaurant starts, with one winner in a race (LIFE-003, 004) | The exact case of cancelling as the kitchen starts | One request is frozen half-way through its change while the other is sent; removing the lock fails these tests |
| The screen never shows a stale price or status as current, and never says "failed" when the outcome is unknown (UI-001 to 003, 011, 015 to 017) | The customer acts on what they see | A fake service with deliberately unrealistic prices; delayed, dropped and out-of-order replies |

**Not covered:** changing a placed order (deferred); one error shape across endpoints (API-002, planned); anything that survives a restart; more than one service instance; real network faults and load (a dropped reply was simulated once by hand);
authentication of the restaurant routes (none, by scope); drift between builder and service if built separately; an automated end-to-end suite; accessibility beyond keyboard use and labels.

---

# Part 2: Design, in one page

## 1. Money, the total, and where pricing lives

**Money** is C# `decimal`, never a float, and the browser only displays what it is sent. Every step rounds half away from zero to cents, so the parts always add up: topping = its rate × the *pizza's* base price → line = unit × quantity → subtotal →
one promotion (never more than the subtotal) → tax 6.5% of the rest → tip (a percentage of subtotal after discount plus tax, or a fixed amount) → total.

**Where:** one library, `LongPizzaOrderBuilder.Pricing`, used by **both** back ends and by no front end. The builder API uses it for the stateless live preview. The order service uses it to price every order, because it issues the receipt and must never trust a total
from a phone or from the builder. Sharing the code, not copying it, means the two cannot disagree, and a test checks that they do not. The cost: built separately, their menus could drift, and a menu version stamp is the fix.

## 2. Adding a size, topping category, or promo code

A new size, crust, topping (with its category and rate) or tax rate is **a row of data** in `PizzaMenu`. The pricing code does not change and the page renders whatever the menu returns; a test adds a whole new size, crust, topping and promo using only data.
A new promo is a `PromotionDescription` row: a code, a type (`Percentage` or `FixedAmount`), a rate or amount, and an optional minimum subtotal, which is its own condition (SAVE5 applies only above $20.00).

*Conditions and conflicts, honestly:* an order carries **one** code, so promos cannot conflict by construction, and the only condition is the minimum subtotal. A richer condition (only with a Large pizza, only on weekdays) is an optional field on the row plus one check in `EvaluatePromotion`.
**Stacking is designed, not built:** promos gain `ConflictsWith` and a priority, the request carries a list of codes, and the engine applies them in priority order, skipping any that conflicts and saying why (as it already does for "not applied: reason").
A discount never exceeding the subtotal, and a bad code costing nothing, carry over.

## 3. The order lifecycle, a retry, and a cancel at the moment the kitchen starts

`NotStarted → Started → ReadyForPickup → Delivered`, or `NotStarted → Cancelled`. One table defines the legal moves; the only way to change an order is the store's `Transition`, which checks the rule and applies the change under one lock.
Walkthrough with real output: [`docs/LIFECYCLE.md`](docs/LIFECYCLE.md).

**A retry after a timeout.** The page sends one random key per order. If the reply is lost it says the order "may or may not have been received" (not "failed"), and on the next press resends the *same* request and key.
The service returns the original order (200, `Idempotency-Replayed: true`); nothing new is created. The same key with a different order is refused (422).

**Cancel just as the restaurant starts.** Both requests are decided one after the other, and exactly one wins. Cancel first: the order is `Cancelled` and the start is refused (409). Start first: the order is `Started` and the cancel is refused
(409, "the restaurant has already started this order") with the order as it now is, which the page shows. There is no third outcome.

**Guarantees, and how:** one order per key, even for simultaneous duplicates (key checked and order saved inside the lock); only legal moves, and a refused move changes nothing (one rule table); one winner in a race (the lock, proved by freezing a request mid-change);
the customer is told the truth (a 409 carries the current order); repeats are harmless; the history is a faithful record. **Not guaranteed:** surviving a restart, more than one instance (the lock is in-process), or protecting the restaurant routes (no authentication).

## 4. What was cut or faked, and what comes next

**Cut or faked:** changing a placed order (deferred as out of scope for this session; a customer can cancel and place again); a database and a message broker (RabbitMQ and PostgreSQL were removed from Compose because nothing used them), so orders and keys live in memory and vanish on restart;
the restaurant is a script, an endpoint and an optional timer, with no screen or login; status by 5-second polling, not push; one promo per order; no payments (a tip is just a number); the restaurant cannot cancel; no estimated time; builder and service share a menu by sharing a build.

**Next, in order:** (1) persistence with a unique key (SQLite, or PostgreSQL added back); (2) authenticate the restaurant routes; (3) a restaurant screen and push updates; (4) a menu version on every price; (5) promo stacking; (6) one error shape (API-002);
(7) changing a placed order, if the business case holds; (8) dependency upgrades. The full backlog is [`docs/FUTURE-WORK.md`](docs/FUTURE-WORK.md); the reasons are in [`docs/decisions/ADR-0010`](docs/decisions/ADR-0010-decisions-outside-the-scope.md).

## 5. AI Usage
I used Claude Sonnet 5.5, in the Claude desktop client to write the code, tests and documentation, and to build, run and test them in its own sandbox. 

One thing I had to fix: After Claude designed the order lifecycle, I tested building the changes and the build failed after 'dotnet build.' 

One thing I checked myself rather than trusting it: I confirmed that an order received by a restaurant is not cancelable when the restaurant starts.  This is confirmed by executing 'docker compose up' from the root directory of the repo, placing an order from the UI, and attempting to cancel the order after the restart starts it.  If the order is canceled by the customer if it's not started, then the cancel wins.

I included automation for invariants where the order lifecycle was most important.  This included placing the order, starting it, and ensuring the order was ready to deliver.  Canceling orders was also implemented.  Changes are not because the rules are complex and require business discussion to realize this feature.
---

More: [design notes and assumptions](docs/DESIGN.md) · [lifecycle](docs/LIFECYCLE.md) · [invariants](docs/INVARIANTS.md) · [decisions](docs/decisions/README.md) · [future work](docs/FUTURE-WORK.md) · [dependency notes](docs/DEPENDENCY-NOTES.md) · [usage audit](docs/USAGE-AUDIT.md) · [setup and deployment details](docs/OPERATIONS.md) · [business rules](docs/BUSINESS-RULES.md) · [project history](docs/HISTORY.md) · [guidance for AI agents](AGENTS.md)
