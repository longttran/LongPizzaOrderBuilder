# ADR-0002 · The builder edits a cart line in place; there is no separate draft

**Status:** Accepted

## Context
The goal is a running total that moves as the customer builds a pizza. The original UI held one pizza in loose state, and
the API supports several line items.

## Decision
Order state is a list of lines plus a *selected* line. The size, crust and topping pickers always edit the selected line.
A new order starts with one default pizza, so a total exists from the first moment. "Add another pizza" appends a line and
selects it. The state holds choices only, never prices (`src/frontend/src/domain/cart.ts`).

## Alternatives considered
- **A separate "current pizza" draft with an "Add to order" button.** Rejected: the total would not move while the customer
  chooses toppings, which defeats the purpose, and it adds a half-committed state to explain and test.
- **One pizza per order.** Rejected: the API and README already model multiple line items.

## Consequences
- Edits are immediately priced (UI-012). Removing the last line leaves a valid empty state (UI-004, UI-005).
- Reducer logic is pure and unit-tested without rendering.
- Quantity limits live in the UI as `MAX_QUANTITY = 10`, mirroring the server's limit. The server stays authoritative. Exposing the limit in `GET /api/menu` would remove the duplicate (ADR-0006).
