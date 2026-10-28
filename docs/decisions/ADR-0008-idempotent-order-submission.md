# ADR-0008 · Idempotent order submission

**Status:** Accepted

> **Amended by [ADR-0009](ADR-0009-separate-order-service.md).** The `Idempotency-Key` header is now **required** on `POST /api/orders` of the order service (a missing key is a 400). The rest stands. The status names below are the earlier ones (Submitted, InProgress).

## Context
A customer presses Place order, the connection drops, and the browser cannot tell whether the server saved the order. Pressing again must not
create a second order, and must not claim the first one failed.

## Decision
- `POST /api/orders` accepts an optional **`Idempotency-Key`** header (1 to 100 characters: letters, digits, `_ - . :`). The browser makes a random
  UUID per checkout attempt.
- The server stores *key → (fingerprint of the request, order id)*. The same key with the same request returns the **original order**:
  **201** the first time, **200** with `Idempotency-Replayed: true` after. The same key with a different request is **422**.
- The **fingerprint** is the trimmed name, the items, the upper-cased trimmed promo code, and the tip, so harmless differences in spelling still match
  and any real change does not.
- **Only successful creations are remembered.** A request that failed validation does not use up its key, so a corrected request can reuse it.
- A replay returns the order's **current** snapshot, so a customer who retries after the kitchen started sees InProgress, not the status at creation. It is never repriced.
- **Browser rules:** one key per attempt; kept across retries of the same order; dropped when the cart or name changes, after a clear refusal (4xx), and after success.
  A timeout, a dropped connection or a 5xx keeps the key. The request times out after 10 seconds, which becomes the "may or may not have been received" message.
- Keys are generated with `getRandomValues`, because `crypto.randomUUID` does not exist on plain-http pages, and the self-hosted site may be served that way.

## Alternatives considered
- **Make the key required.** Rejected for now: it would break clients and the existing integration tests that do not send one. Reasonable once the UI is the only client.
- **Deduplicate by content (same order within N minutes).** Rejected: two identical orders can be legitimate, such as a family ordering twice.
- **Client-chosen order id with `PUT /api/orders/{id}`.** A good REST-style alternative, with the same guarantees; rejected here because it changes the contract more than a header does.
- **Return the response exactly as first sent.** Rejected: the current status is more truthful to the customer, and the price is stored so it is identical anyway.

## Consequences
- Covered by ORD-004 and UI-015. Concurrent duplicates are covered by a test that freezes the first attempt mid-save.
- Keys live as long as the process, so memory grows with orders, and a restart forgets them (see "What is not guaranteed" in `docs/LIFECYCLE.md`). Expiry belongs with persistence.
