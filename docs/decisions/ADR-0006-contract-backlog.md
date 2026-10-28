# ADR-0006 · Contract changes considered and deferred

**Status:** Proposed (nothing here is implemented; each item needs a decision and its own tests)

These were found during review. They change the API contract, so they were left for a deliberate decision rather than slipped in.

| Change | Why | Cost / risk |
|---|---|---|
| Give sizes and crusts stable `id`s (toppings already have them) | Display names are used as identifiers today | Breaking change to the menu and order contracts |
| Remove the legacy single-pizza order fields (`size`, `crust`, `toppings`, `quantity`) and the compatibility properties on `PizzaOrder` | Two ways to say the same thing | The new UI no longer uses them; check any other client first |
| Return RFC 7807 problem details everywhere (API-002) | Preview returns `PricingPreviewResponse` on 400, orders return `{ errors }` | UI error handling changes in one place |
| Expose `maxQuantityPerLine` and tip limits in `GET /api/menu` | The UI duplicates the quantity limit | Additive |
| Use enums for tip type and order status | Strings invite typos | Needs a JSON converter; additive on the wire |
| Idempotency key on `POST /api/orders` (ORD-004) | Makes a retry after an unknown outcome safe (UI-011) | Needs persistence to be meaningful |
| ~~Remove `GET /api/orders`~~ **Resolved in ADR-0009:** customers can no longer list orders; only the restaurant door can | The list shows every customer's name. The project assumes no authentication, so protecting it is out of scope; removing it is the proportionate fix | Behaviour change |
| Add the "tampered totals are ignored" integration test (API-001) | Closes a README gap | Needs the `WebApplicationFactory` run locally |

## Dropped from scope
- **Order events, a transactional outbox and a RabbitMQ worker (formerly EVT-001).** They exist to feed a kitchen or delivery system, which this
  project does not build. The order lifecycle is kept as in-process state with a recorded history (LIFE-001, LIFE-002). The `worker`, RabbitMQ and
  PostgreSQL services already in Compose are pre-existing scaffolding. Removing them from the default stack is recommended but was not done here,
  because it changes your working Docker setup.
