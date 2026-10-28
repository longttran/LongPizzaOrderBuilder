#!/usr/bin/env bash
# Walks through what the order service guarantees on an unreliable connection, against a running service, and prints what happens:
#   1. a customer retries a submission after a timeout
#   2. a delayed, out-of-date update arrives after a newer one
#   3. a customer cancels at the same moment the restaurant starts the order
# Usage: scripts/demo-order-service.sh [ORDER_SERVICE_URL]      (default http://localhost:8090; needs bash and curl)
set -euo pipefail
BASE="${1:-http://localhost:8090}"
JSON='Content-Type: application/json'
field() { grep -o "\"$1\":\"[^\"]*\"" | head -1 | cut -d'"' -f4; }
num() { grep -o "\"$1\":[0-9.]*" | head -1 | cut -d: -f2; }
events() { grep -o '"event":"[A-Za-z]*"' | cut -d'"' -f4 | paste -sd' ' - | sed 's/ / -> /g'; }
BODY() { printf '{"customerName":"%s","items":[{"size":"Medium","crust":"Classic","toppings":["pepperoni"],"quantity":%s}],"tip":{"type":"None"}}' "$1" "${2:-1}"; }
show() { curl -s -o /tmp/resp -w '  HTTP %{http_code}: ' "$@"; grep -o '"errors":\["[^"]*"' /tmp/resp | head -1 | cut -d'"' -f4; }
KEY="demo-$(date +%s)-$RANDOM"; NAME="Demo $RANDOM"

echo "=== 1. Retry after a timeout ==============================================="
echo "The customer sends the order with Idempotency-Key: $KEY"
first=$(curl -s -D /tmp/h1 -H "$JSON" -H "Idempotency-Key: $KEY" -d "$(BODY "$NAME")" "$BASE/api/orders")
echo "  attempt 1 -> HTTP $(head -1 /tmp/h1 | cut -d' ' -f2), order $(echo "$first" | field id), status $(echo "$first" | field status)"
echo "  ...the reply is lost on the way back. The phone times out and sends the SAME request again:"
second=$(curl -s -D /tmp/h2 -H "$JSON" -H "Idempotency-Key: $KEY" -d "$(BODY "$NAME")" "$BASE/api/orders")
echo "  attempt 2 -> HTTP $(head -1 /tmp/h2 | cut -d' ' -f2), order $(echo "$second" | field id), $(grep -i '^idempotency-replayed' /tmp/h2 | tr -d '\r')"
count=$(curl -s "$BASE/api/restaurant/orders" | grep -o "\"customerName\":\"$NAME\"" | wc -l | tr -d ' ')
echo "  orders held for \"$NAME\": $count   (one, not two)"
echo "  Reusing that key for a DIFFERENT order is refused:"
show -H "$JSON" -H "Idempotency-Key: $KEY" -d "$(BODY "Someone Else")" "$BASE/api/orders"
ID=$(echo "$first" | field id)

echo
echo "=== 2. Update, and a delayed older update ===================================="
echo "The customer changes the order to 2 pizzas (based on version 1):"
curl -s -D /tmp/h3 -X PUT -H "$JSON" -H 'If-Match: "1"' -d "$(BODY "$NAME" 2)" "$BASE/api/orders/$ID" > /tmp/b3
echo "  HTTP $(head -1 /tmp/h3 | cut -d' ' -f2), version now $(num version < /tmp/b3), total $(num total < /tmp/b3) (repriced by the service)"
echo "A second change to 3 pizzas (based on version 2):"
curl -s -X PUT -H "$JSON" -H 'If-Match: "2"' -d "$(BODY "$NAME" 3)" "$BASE/api/orders/$ID" > /tmp/b4
echo "  version now $(num version < /tmp/b4), total $(num total < /tmp/b4)"
echo "The FIRST change, delayed on a bad connection, finally arrives (still based on version 1):"
show -X PUT -H "$JSON" -H 'If-Match: "1"' -d "$(BODY "$NAME" 2)" "$BASE/api/orders/$ID"
echo "Resending the 3-pizza change that was already applied is harmless:"
curl -s -o /tmp/b5 -w '  HTTP %{http_code}' -X PUT -H "$JSON" -H 'If-Match: "2"' -d "$(BODY "$NAME" 3)" "$BASE/api/orders/$ID"; echo ", version still $(num version < /tmp/b5)"

echo
echo "=== 3. Cancel at the moment the restaurant starts ============================"
for round in 1 2 3 4 5; do
  id=$(curl -s -H "$JSON" -H "Idempotency-Key: race-$KEY-$round" -d "$(BODY "Race $round")" "$BASE/api/orders" | field id)
  curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/orders/$id/cancel" > /tmp/c.code &
  curl -s -o /dev/null -w '%{http_code}' -H "$JSON" -d '{"to":"Started"}' "$BASE/api/restaurant/orders/$id/transitions" > /tmp/k.code &
  wait
  final=$(curl -s "$BASE/api/orders/$id")
  echo "  round $round: customer cancel -> HTTP $(cat /tmp/c.code) | restaurant start -> HTTP $(cat /tmp/k.code) | final status $(echo "$final" | field status) | history: $(echo "$final" | events)"
done
echo "  Exactly one wins each round; the other gets 409 and the true status."
echo "Once the restaurant has started, the customer can no longer change or cancel:"
id=$(curl -s -H "$JSON" -H "Idempotency-Key: late-$KEY" -d "$(BODY "Too Late")" "$BASE/api/orders" | field id)
curl -s -o /dev/null -H "$JSON" -d '{"to":"Started"}' "$BASE/api/restaurant/orders/$id/transitions"
show -X POST "$BASE/api/orders/$id/cancel"
show -X PUT -H "$JSON" -H 'If-Match: "2"' -d "$(BODY "Too Late" 4)" "$BASE/api/orders/$id"
