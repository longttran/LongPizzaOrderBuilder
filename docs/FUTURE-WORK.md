# Future work

Nothing here is built. Each item was left out on purpose to stay in scope. The first table is the whole backlog on one page; the detail for changing a placed order follows it, and the reasoning for most other items is in [ADR-0010](decisions/ADR-0010-decisions-outside-the-scope.md).

## Backlog at a glance

| # | Item | Why it matters | Where it is described |
|---|---|---|---|
| 1 | **Changing a placed order** (update) | The brief lists it. Deferred as out of scope for this session; needs a design and a business discussion first | [below](#changing-a-placed-order-deferred) |
| 2 | **Persistence** for orders and idempotency keys | A restart currently loses both, so a retry that spans a restart could duplicate an order. PostgreSQL was removed from Compose and would be added back, or use SQLite | ADR-0010 |
| 3 | **Authenticate the restaurant routes** | Anyone who can reach the service can start or deliver any order | ADR-0010 |
| 4 | **A restaurant screen, and push updates** instead of 5-second polling | Staff should not need `curl`; customers would see "Started" at once | ADR-0010 |
| 5 | **Menu version stamped on every price** | Lets the builder and the service detect that they were deployed from different builds | ADR-0010 |
| 6 | **Promo stacking and conflicts** | Today one code per order, so promos cannot conflict. Richer rules need priorities and `ConflictsWith` | README, section 2 |
| 7 | **One error shape across every endpoint** (API-002, the one planned invariant) | One way to read an error | [INVARIANTS.md](INVARIANTS.md) |
| 8 | **Message broker and events** | RabbitMQ and the placeholder `worker` were removed because nothing used them. Add them back only when something consumes from a broker, for example a kitchen integration (out of scope) | ADR-0010 |
| 9 | **Dependency upgrades**: `vite` to 6.4.5 (safe, tested), `vitest` to 5, ESLint to 10 | Clears the 4 dev-tooling vulnerabilities and an unsupported ESLint | [DEPENDENCY-NOTES.md](DEPENDENCY-NOTES.md) |
| 10 | **Bind the dev Vite port to `127.0.0.1`** in `compose.yaml` | The dev server is currently published on all interfaces | DEPENDENCY-NOTES.md |
| 11 | **`restaurant.sh` accepts the short id** shown on the page | Today only the full 32-character id works; the short one gives `404` | README |
| 12 | **Automated end-to-end test**, and tests for real network faults | A dropped reply was simulated once by hand, not in a test | [DESIGN.md](DESIGN.md), section 10 |
| 13 | **Key expiry, rate limiting, structured logs and metrics** | Keys currently live for the life of the process | ADR-0010 |
| 14 | **Estimated time, notifications, restaurant-side cancel and out-of-stock** | A real kitchen needs them | ADR-0010 |

## Changing a placed order (deferred)

**Status:** not built, and **deferred as out of scope for this session.** A working version was prototyped, tested and then **removed from this delivery on purpose**, so the first delivery stays focused on the
order lifecycle and the cancel path. Building it needs a **design discussion** and a **business discussion that justifies the work**. This page holds what that discussion needs.

> The brief lists "update the order" alongside cancel, so this is a visible scope decision, not an oversight. What ships today: a customer can **cancel** an order, but only while it is Not Started.
> To change an order today, the customer cancels it and places a new one (see "What exists today" below).

### What exists today

- A customer can send an order, query its status, and **cancel it while it is Not Started**. Cancel is safe to repeat and safe against a race with the restaurant starting the order (see [LIFECYCLE.md](LIFECYCLE.md)).
- **Cancel and place again is already a way to change an order.** It uses code that is built and tested, creates a clean new receipt, and cannot partly apply. This is the baseline any "change order" feature has to beat.
- The service reprices every order it receives, and keeps the items and accepted price fixed once the order is placed (ORD-003).

### Business discussion: is it worth building?

The case for building it rests on what "cancel and place again" does **not** give the customer. These are the questions to settle, with the people who run the restaurant, before any design work:

1. **What does the customer lose by cancelling and reordering?** Candidates: their place in the queue, the order number they already gave to someone, a tip or promotion they entered, the effort of rebuilding a large order. Which of these matter to real customers?
2. **How often does it happen, and how much does it matter?** How many customers cancel and reorder within a few minutes today? (No data exists yet; the order history will show this once there is persistence.) If it is rare, the cost may not be justified.
3. **What may be changed?** Everything (a full replacement), or only some things (quantities, toppings, but not the name or promo)? Fewer options mean fewer rules to get right.
4. **What happens to the price?** The total can go up or down. Does the customer see the new total and confirm before it is applied? Is a promotion re-checked (SAVE5 needs a subtotal over $20, so removing a pizza can make it stop applying)? Does a tip stay as a percentage or as a fixed amount?
5. **Does a change keep the customer's place in the queue?** If yes, a customer could keep changing an order to hold a place; do we limit the number of changes, or the time window? If no, the feature is hardly different from cancel and reorder.
6. **How long is the window?** "Only while Not Started" can mean seconds in a busy kitchen. Is a short grace period after the restaurant starts worth the complexity, or is it better to be strict and clear?
7. **Does the restaurant need to know?** A changed order may need a visible mark so the restaurant does not start from an out-of-date copy.
8. **What does it cost?** Build, test and review effort, plus ongoing cost: more states to explain to customers, more cases for support. A rough estimate belongs here once the scope above is chosen.
9. **When payments exist,** a change means changing an authorisation or issuing a refund. Is that in the plan? (It is out of scope for this project, but it affects the design.)

**Suggested decision rule (a proposal to discuss, not a fact):** build it only if the restaurant confirms that losing the queue position or the order reference is a real problem, and the frequency of cancel-and-reorder justifies the added rules.
If the answer is "customers can simply cancel and reorder", record that decision and close this item.

### Design discussion: what to settle

Starting positions are from the removed prototype; each is open to challenge.

| Question | Options | Prototype's starting position |
|---|---|---|
| Replace or patch? | Replace the whole order; change named fields; add a new "revision" and keep the old | Replace the whole order (`PUT`), repriced by the service |
| How is a stale or repeated update handled? | A version the client must quote (`If-Match`); an idempotency key per update; last write wins | A version in `If-Match`. Identical contents are a harmless repeat; an older update is refused with 412 and the current order; a missing header is 428 |
| What does the history record? | Only that it changed; or the before and after | An "Updated" event. A real audit trail needs the previous contents too |
| Do accepted prices stay fixed? | ORD-003 would change to "items and price change only through an accepted update", or each revision is kept as its own immutable receipt | Overwrite in place. Keeping revisions is safer and is the better answer if refunds are ever added |
| Race with the restaurant starting | Same lock as cancel, so exactly one wins | Same lock; an update and a start cannot both be applied, and a started order is never changed |
| What if the menu changed since the order was placed? | Reprice with the new menu; refuse; reprice only if the total does not go up | Reprice. Needs a menu version stamp so this is detectable (ADR-0010) |
| Customer experience | Load the order back into the builder; or a smaller "edit" screen | Load it into the builder and save. The page keeps a version, and shows the service's reason if the restaurant started first |
| Concurrency across devices | One device only; or two phones editing the same order | Version check catches it; the page reloads the latest and asks the customer to review |

Questions that are not answered by the prototype: how the new total is confirmed, whether changes are limited, and what the restaurant sees.

### What was prototyped (and removed)

- **Service:** `PUT /api/orders/{id}` with `If-Match: "<version>"`; an `OrderStore.Update` that checks status, version and content under the same lock as cancel and start; an `ETag` header; a `version` counter; an "Updated" history event.
- **Page:** a "Change this order" button, an edit mode that loads the order into the builder, a "Save changes" button, version-aware error handling (409 restaurant started, 412 changed elsewhere, unknown outcome with a safe retry).
- **Tests:** 16 backend tests (rules, repeats, stale updates, the update-versus-start race frozen mid-change, required headers) and 11 frontend tests. Removing the lock from `Update` failed the race tests.
- **Where it is:** a patch, `future-change-orders.patch`, handed over with this change, applies on top of it. It is **not in the repository**; if it is lost, this page and the invariants below are enough to rebuild it. The patch covers code, tests and scripts only, not documents: after applying it, `node scripts/check-invariants.mjs` will fail until ORD-005, ORD-006 and UI-018 (drafted below) are put back in `docs/INVARIANTS.md`. That is the registry doing its job, not a defect in the patch.

### Invariants to add if it is built (drafted, not in the registry)

These IDs are retired and must not be reused for anything else.

- **ORD-005 · An update is accepted only while NotStarted, and is repriced.** The service reprices an update itself. If the order is Started, ReadyForPickup, Delivered or Cancelled the update is refused, nothing changes, and the reply carries the order as it now is.
- **ORD-006 · An update is safe to repeat and cannot be applied out of order.** An identical update succeeds without a second change. An update based on an out-of-date version is refused and never overwrites the newer order. An update that does not say which version it is based on is refused.
- **UI-018 · Changing a placed order is safe on a bad connection.** The change is offered only while the service says it can be made. If the restaurant has started in the meantime the customer sees the reason and the real status. If the order changed elsewhere the latest version is loaded. If the save cannot be confirmed, the customer is told and pressing Save again is safe.
- **Changes to existing invariants:** ORD-003 (see above), LIFE-002 (history includes updates, and the order's version equals the number of events), LIFE-003 (an update and a start cannot both win), LIFE-004 (the customer can change as well as cancel), API-003 and API-004 (the update endpoint and its header).

### If it is approved

1. Settle the business questions above and write down the decisions (an ADR).
2. Decide revisions versus overwrite, and the menu-version question, because they shape the data.
3. Write the invariants first, then the tests (including the update-versus-start race, frozen mid-change), then the code.
4. Add the page changes last, so the service rules are fixed before the screen depends on them.

## Other deferred work

See the backlog table at the top. The reasons, and what each would change, are in [ADR-0010](decisions/ADR-0010-decisions-outside-the-scope.md).
