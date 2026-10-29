# ADR-0004 · Invariants have IDs, and tests carry them

**Status:** Accepted

## Context
The README held a prose list of invariants and a test checklist that drifted from reality (items were unchecked although tests
existed). Prose cannot be verified, so it cannot protect the code.

## Decision
`docs/INVARIANTS.md` is the registry: each invariant has an ID, a falsifiable statement and a status. Tests reference IDs
(C# `Trait`, TypeScript test titles). `scripts/check-invariants.mjs`, run in CI, fails when an enforced invariant has no
test, when a test cites an unknown ID, or when a planned invariant already has tests.

Pricing invariants are additionally checked against an **independent oracle** (integer cents, no shared code with the
service) over a fixed-seed corpus of about 20,000 orders, so a mistake cannot be copied into both the code and its test.

## Alternatives considered
- **Keep the README checklist.** Rejected: it drifted.
- **Property-based testing libraries (FsCheck, fast-check).** Good fit later; a seeded corpus gives determinism with no new
  dependency today. Swap in when shrinking of failing cases becomes valuable.

## Consequences
- The checker proves a test *mentions* an invariant, not that the test is good. Quality is checked by mutation: deliberately
  break the code and confirm the named tests fail. While this was introduced, mutation checking exposed a UI-001 test that never asserted the tax amount, and a pricing test that only exercised `Math.Round`.
- Adding an invariant is cheap and mechanical, which is the point.
