#!/usr/bin/env bash
# Puts a few sample orders into a running order service, in different statuses, so there is something to look at.
# Usage: scripts/seed.sh [ORDER_SERVICE_URL]      (default http://localhost:8090; needs bash and curl)
set -euo pipefail
BASE="${1:-http://localhost:8090}"
JSON='Content-Type: application/json'
id_of() { grep -o '"id":"[a-f0-9]*"' | head -1 | cut -d'"' -f4; }
place() { # name size toppings-json
  curl -s -H "$JSON" -H "Idempotency-Key: seed-$RANDOM-$RANDOM-$(date +%s)" \
    -d "{\"customerName\":\"$1\",\"items\":[{\"size\":\"$2\",\"crust\":\"Classic\",\"toppings\":$3,\"quantity\":1}],\"tip\":{\"type\":\"None\"}}" "$BASE/api/orders" | id_of
}
move() { curl -s -o /dev/null -H "$JSON" -d "{\"to\":\"$2\"}" "$BASE/api/restaurant/orders/$1/transitions"; }

a=$(place "Ada (not started)" Small '["pepperoni"]')
b=$(place "Ben (started)" Medium '["sausage","onions"]');            move "$b" Started
c=$(place "Cy (ready for pickup)" Large '["bacon"]');               move "$c" Started; move "$c" ReadyForPickup
d=$(place "Dee (delivered)" Medium '[]');                           move "$d" Started; move "$d" ReadyForPickup; move "$d" Delivered
e=$(place "Eli (cancelled)" Small '["pineapple"]');                 curl -s -o /dev/null -X POST "$BASE/api/orders/$e/cancel"
echo "Seeded 5 orders. Not started: $a  (you can still change or cancel it)"
ORDER_SERVICE_URL="$BASE" "$(dirname "$0")/restaurant.sh" list
