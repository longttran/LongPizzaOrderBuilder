# Architecture decision records

Short records of decisions that are costly to rediscover: what was decided, why, and what was rejected. Write a new
one when a choice would surprise a future reader or when you reverse an earlier one. Never edit an accepted record's
decision; add a new record that supersedes it.

| ADR | Decision | Status |
|---|---|---|
| [0001](ADR-0001-server-authoritative-pricing.md) | The server is the only source of price | Accepted |
| [0002](ADR-0002-builder-edits-the-cart-line.md) | The builder edits a cart line in place; there is no separate draft | Accepted |
| [0003](ADR-0003-pricing-preview-semantics.md) | How the UI keeps a server price in step with the order | Accepted |
| [0004](ADR-0004-invariant-registry.md) | Invariants have IDs, and tests carry them | Accepted |
| [0005](ADR-0005-menu-driven-pricing.md) | Pricing reads its rules from injected menu data | Accepted |
| [0006](ADR-0006-contract-backlog.md) | Contract changes considered and deferred | Proposed |
| [0007](ADR-0007-order-lifecycle-and-concurrency.md) | Order lifecycle, who may do what, and how races are decided | Accepted (amended by 0009) |
| [0008](ADR-0008-idempotent-order-submission.md) | Retrying an order submission is safe | Accepted (amended by 0009) |
| [0009](ADR-0009-separate-order-service.md) | A separate order service, with a shared pricing library | Accepted |
| [0010](ADR-0010-decisions-outside-the-scope.md) | Decisions outside the scope that would have helped | Proposed |

Template: Context, Decision, Alternatives considered, Consequences.
