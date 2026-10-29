# ADR-0001 · The server is the only source of price

**Status:** Accepted (records a decision already in the README)

## Context
The customer sees a running total as they build an order, and the same rules decide what they are charged. Two
implementations of the price formula (browser and server) drift apart, and a client that sends prices can be tampered with.

## Decision
Pricing is implemented once, in `PricingService`. The browser sends choices (size, crust, toppings, quantity, promo
code, tip choice) and displays the amounts the server returns. Preview and order submission call the same method.

## Alternatives considered
- **Duplicate the formula in React for instant totals.** Rejected: guaranteed drift, and every rule change is made twice.
  A local estimate could be added later only if it is clearly labelled and reconciled with the server (not currently needed;
  the local round trip is a few milliseconds).
- **Trust client totals.** Rejected: unsafe.

## Consequences
- The UI needs an explicit pending state while a price is being calculated (ADR-0003).
- Invariants UI-001 and ORD-002 protect this decision from both sides.
- One narrow exception: the topping cards show a per-topping price *hint* derived from menu data. Hints are never summed.
