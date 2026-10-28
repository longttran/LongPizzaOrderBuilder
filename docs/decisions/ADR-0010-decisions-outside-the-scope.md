# ADR-0010 · Decisions outside the scope that would have helped

**Status:** Proposed (none of this is built; it records what was left out on purpose and what each omission costs)

> **Update:** RabbitMQ, PostgreSQL and the placeholder `worker` were removed, because no code used them. The backlog is in [FUTURE-WORK.md](../FUTURE-WORK.md).

The scope is one restaurant, anonymous customers, and an order service with a simulated restaurant side. These choices sit outside it. Each would have made the system safer or easier to run,
and each was left out to stay in the timebox.

| Decision that was out of scope | Why it would have helped | What it would change |
|---|---|---|
| **Changing a placed order** (update) | The brief lists it. It would let a customer keep their place and order reference instead of cancelling and reordering | Needs a design and a business discussion first; the questions, the starting design and the withdrawn invariants are in [FUTURE-WORK.md](../FUTURE-WORK.md) |
| **Persist orders and idempotency keys** (SQLite for one node, or PostgreSQL, which was removed from Compose and would be added back), with a unique constraint on the key and a row version | A restart no longer loses orders or forgets keys, so a retry that spans a restart cannot duplicate; several service instances become safe | `OrderStore` becomes a repository; the lock becomes a transaction; `Version` maps to the row version |
| **Authenticate the restaurant routes** (an API key, or only reachable on a private network) | Today anyone who can reach the service can start, advance or deliver any order | A filter on `/api/restaurant`; the actor could then be a named person in the history |
| **Rate limiting** on create and cancel | A phone stuck in a retry loop, or abuse, cannot flood the service | Middleware |
| **A menu version stamped on every price** | The builder and the service could detect that they were deployed from different builds, instead of quietly disagreeing | A `menuVersion` in the preview and the order; the service rejects a mismatch |
| **Push updates (server-sent events) instead of polling every 5 seconds** | The customer sees the restaurant start the order at once, so fewer cancel attempts hit a 409 | A stream endpoint and a small page change |
| **Key expiry** (for example 24 hours) | Keys no longer grow without limit | A sweep, or a TTL column |
| **Restaurant-side cancel, out-of-stock and refunds** | A real kitchen sometimes cannot make an order; it needs a way to say so | New transitions and actor rules; the lifecycle table grows |
| **A restaurant screen** | Staff should not need curl | A page, plus the authentication above |
| **Estimated time and notifications** | The customer wants to know when | A field on the order; push or SMS |
| **One error shape** (RFC 7807) and **contract tests** between the page and the service | One way to read an error; changes to the API cannot silently break the page | API-002; a shared contract file |
| **Structured logs with the idempotency key as a correlation id, plus basic metrics** | Support can follow one customer's retries; rising 409 and 412 rates show a problem | Logging and counters |
| **A message broker and a worker** (both removed) | Nothing used them, which suggested an integration that did not exist | Add a broker and a worker back only when something consumes from the broker (for example a kitchen integration, itself out of scope) |

The two that matter most for correctness are the first (persistence with a unique key) and the fourth (a menu version), because they are the only two ways the current design can give a *wrong* answer rather than just a less convenient one.
