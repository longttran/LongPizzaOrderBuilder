# ADR-0009 · A separate order service, with a shared pricing library

**Status:** Accepted. Amends ADR-0007 and ADR-0008 (the status names, who may cancel, and the key being required). Changing a placed order was prototyped and then deferred; see [FUTURE-WORK.md](../FUTURE-WORK.md).

## Context
The brief asks for an order service that runs as a **separate process** from the order builder and talks to it over the network. Through it, a customer can send an order and get an id,
query its status (Not Started, Started, Ready for Pickup, Delivered), and update or cancel it, but only while it is Not Started. **This delivery builds query and cancel; update is deferred** (see Decision). The restaurant side advances the status, simulated however we like.
Customers are on unreliable mobile connections, so requests time out and are retried.

## Decision
- **Three back-end projects.** `Pricing` is a class library: the menu data and the one pricing engine. `Api` is the order builder's back end: the menu and a stateless price preview. `OrderService` is a separate
  process that owns orders, their lifecycle and the restaurant's side. The page talks to the builder API for the menu and preview, and to the order service for orders, over HTTP (with a CORS allow-list that exposes `Idempotency-Replayed`).
- **Pricing runs in both back ends, from the same code.** The builder previews with it; the service charges with it, on every order it receives. The service never trusts a total from a phone or from the builder. A test
  compares the builder's preview with the service's accepted total.
- **Statuses** are the brief's four, plus `Cancelled` so a cancelled order can say so: `NotStarted → Started → ReadyForPickup → Delivered`, or `NotStarted → Cancelled`.
  The **customer** may cancel only while NotStarted. The **restaurant** moves one step forward and cannot cancel (restaurant cancellation is outside the brief).
- **Two doors.** `/api/orders` is the customer's; `/api/restaurant/orders` is the restaurant's. The role comes from the door, never from the request. A customer cannot list orders; the restaurant can.
- **Update (changing a placed order) is deferred**, not built. The brief lists it, so this is a scope decision: it needs a design discussion (replace or patch, versions, repricing, history) and a business discussion that justifies it (a customer can already cancel and place again). A prototype was built, tested and removed; the questions and the starting design are in [FUTURE-WORK.md](../FUTURE-WORK.md).
- **Create requires `Idempotency-Key`** (ADR-0008 made it optional; with one client and a brief that stresses retries, a missing key is now a 400).
- **The restaurant is simulated** three ways, all through the same checked `Transition`: an endpoint, `scripts/restaurant.sh`, and an optional timer (`Restaurant:AutoAdvanceSeconds`, off by default).
- **Concurrency** stays as ADR-0007: one lock around every read-check-write, immutable snapshots.

## Alternatives considered
- **The builder API forwards orders to the service, so the service trusts the builder's price.** Rejected: the customer's status and cancel calls reach the service anyway, an extra hop adds another place to time out, and trusting a price from a caller is the bug the whole design avoids.
- **Copy the pricing code into the service.** Rejected: two copies drift. A shared library cannot.
- **Price only in the service, and have the builder's preview call it.** The simplest way to rule out drift, and a reasonable choice. Not chosen because the live total would then depend on the service being up and be requested on every click; the cost of the choice is that the two back ends must be deployed from the same build.
- **A queue or events (RabbitMQ) between them.** Outside the scope: there is no kitchen to feed. The brief wants a network call a customer can retry.
- **Let the restaurant cancel.** Outside the brief; see ADR-0010.

## Consequences
- Invariants ORD-002 to ORD-004, LIFE-001 to LIFE-006, API-003 and API-004 protect this. ORD-005, ORD-006 and UI-018 are withdrawn with the update feature.
- Orders and keys are in memory in the service, so a restart loses them (ADR-0010 says what would fix that).
- The builder and service share a menu by sharing a build. They could drift if deployed separately; a menu version stamp would detect it (ADR-0010).
