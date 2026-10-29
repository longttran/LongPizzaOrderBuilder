# ADR-0003 · How the UI keeps a server price in step with the order

**Status:** Accepted

## Context
Every change triggers a pricing request. Responses can be slow or arrive out of order, requests can fail, and a number on
screen must never look current when it is not.

## Decision
`usePricingPreview(request)` follows these rules:
1. The order is serialised to a **key**. A response is *current* only if its key equals the key of the order on screen.
2. Requests are **debounced** (250 ms) and the previous one is **aborted**. A response from an aborted request is also
   ignored in the success path, because a mocked or buggy transport may still resolve it (a unit test found exactly this).
3. While not current, the last total stays visible but dimmed and labelled "Updating…". Ordering is disabled.
4. A pizza whose choices changed shows "…" instead of a price. A price is matched to a pizza by its *configuration*,
   never by list position, so an unchanged pizza keeps its true price while others reprice.
5. HTTP 400 from the preview endpoint is a normal answer carrying `errors`, so it is returned, not thrown. Other failures
   become an "unavailable" state with no total.
6. An empty order sends no request.

## Alternatives considered
- **Blank the total while pending.** Rejected: the total would flicker on every click.
- **Show the old total as if current.** Rejected: it violates UI-002 and risks the customer ordering at a price that is out of date.
- **A data-fetching library (React Query).** Reasonable later. Not adopted now because the behaviour needed is small and
  specific, and an extra dependency would hide the rules above.

## Consequences
Covered by UI-002, UI-003, UI-004 and UI-006. Changing the debounce or the matching rule requires updating those tests.
