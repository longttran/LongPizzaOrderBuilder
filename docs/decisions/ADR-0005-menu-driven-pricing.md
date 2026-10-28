# ADR-0005 · Pricing reads its rules from injected menu data

**Status:** Accepted

## Context
`PricingService` read the static `PizzaMenu.Default` directly and hardcoded the promotion rules (`0.10`, `5.00`, `> 20`) even
though the menu already listed them. A rule lived in two places, and the README goal "menu options are data" was not true for promotions.

## Decision
`PricingService(PizzaMenu menu)` takes the menu by injection (a parameterless constructor keeps `new PricingService()` working
for existing tests). Promotion rules are read from `PromotionDescription`, which gained `MinimumSubtotalExclusive`.
Messages shown to customers are unchanged. A discount is clamped so it can never exceed the subtotal.

## Alternatives considered
- **A general rule engine for promotions.** Rejected for now (see README): two promotion types, `Percentage` and
  `FixedAmount`, plus an optional threshold cover the need. Revisit when a third kind appears.
- **Load the menu from the database now.** Deferred to the persistence work; injection makes that a small change.

## Consequences
- PRC-015 tests prove a different menu changes prices with no code change.
- `GET /api/menu` gains an additive field `minimumSubtotalExclusive` (null when there is no threshold).
