# ADR-0007 · Order lifecycle and concurrency

**Status:** Accepted

> **Amended by [ADR-0009](ADR-0009-separate-order-service.md).** The lifecycle moved into a separate order service, the statuses became Not Started, Started, Ready for Pickup, Delivered and Cancelled, only the customer cancels (and only while Not Started), and the restaurant moves one step forward. (Changing a placed order was deferred; see [FUTURE-WORK.md](../FUTURE-WORK.md).) The text below is the original reasoning, written with the earlier status names (Submitted, InProgress, Completed). The concurrency design (one lock, immutable snapshots, deterministic race tests) is unchanged.

## Context
Orders must move through a small, checkable life, and two people can act on the same order at once: a customer pressing Cancel while the
kitchen presses Start. The project has no authentication, one restaurant, and an in-memory store. The stated priority is a defensible lifecycle.

## Decision
- **Statuses:** `Submitted → InProgress → Completed`, with `Cancelled` allowed from Submitted and InProgress. Completed and Cancelled are final.
  The rules are one table in `OrderLifecycle`. A delay is not a status; it would be a note on InProgress.
- **Who may do what:** the restaurant may make any legal move; a customer may only cancel, and only from Submitted. The role is chosen
  by the **endpoint** (`/cancel` versus `/transitions`), never by a field the caller sends, because with no authentication a claimed role means nothing.
- **One lock.** `PizzaOrderStore` guards creation, key lookup and every transition with a single lock. Checking the status and applying the
  change happen inside it, so concurrent requests are decided one after the other and exactly one wins.
- **Immutable snapshots.** `PizzaOrder` is a record. A change makes a new snapshot (new status, new history entry) and swaps it in. Readers
  never see a status that disagrees with the history, and the accepted price and items can never be altered.
- **Asking for the current status is a no-op success**, so a repeated cancel or start is harmless and adds no history.
- **A refusal is 409 with the order as it now is**, so the caller can show the truth.
- **The clock is injected** (`TimeProvider`). Tests use it for predictable history times, and a test clock that pauses inside the critical
  section makes races deterministic.

## Alternatives considered
- **Optimistic concurrency (a version or `If-Match`), where the client says which status it saw.** Rejected: the server already decides which
  move is legal from the *current* state, so a client-supplied version adds a round trip without adding safety. It becomes the right tool with a database.
- **A lock per order.** Rejected as unneeded: one restaurant's order volume does not contend on one lock, and one lock is easier to reason about.
- **Database row locks or serializable transactions.** The right answer for several API instances. Deferred to the persistence work; the store's
  interface (`Create`, `Transition`) is the seam.
- **Letting the customer cancel while InProgress.** Rejected as a business rule (the food is being made). **This is an assumption to confirm with the restaurant.**
- **Taking the role from a request field or header.** Rejected: unauthenticated, so it would only look like access control.

## Consequences
- Invariants LIFE-001 to LIFE-005 and ORD-003 protect this. LIFE-003 is tested by freezing one request mid-change, because on a one-core machine
  timing-based race tests passed even with the lock removed (found by deliberately removing it).
- `POST /api/orders/{id}/transitions` is open to anyone who knows an order's id. That is stated wherever it is documented.
- A single lock does not protect several API instances. See "What is not guaranteed" in `docs/LIFECYCLE.md`.
