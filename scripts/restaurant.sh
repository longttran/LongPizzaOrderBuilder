#!/usr/bin/env bash
# The restaurant's side, as a command line. The order service lets the restaurant see orders and move them along;
# this wraps those calls. Needs bash and curl.
#
#   scripts/restaurant.sh list [STATUS]     show orders (STATUS: NotStarted, Started, ReadyForPickup, Delivered, Cancelled)
#   scripts/restaurant.sh start ORDER_ID    NotStarted     -> Started
#   scripts/restaurant.sh ready ORDER_ID    Started        -> ReadyForPickup
#   scripts/restaurant.sh deliver ORDER_ID  ReadyForPickup -> Delivered
#
# ORDER_ID is the FULL 32-character id. The page shows only its first 8 characters, which are not enough: run 'list' to get the full id.
#
# The service address is ORDER_SERVICE_URL (default http://localhost:8090). Customers cannot call these routes;
# there is no authentication in this project, so keep the service off the public internet.
set -euo pipefail
BASE="${ORDER_SERVICE_URL:-http://localhost:8090}/api/restaurant/orders"
move() { curl -s -w '\n[HTTP %{http_code}]\n' -H 'Content-Type: application/json' -d "{\"to\":\"$2\"}" "$BASE/$1/transitions"; }
case "${1:-}" in
  list)    curl -s "$BASE${2:+?status=$2}" | grep -o '"id":"[a-f0-9]*","customerName":"[^"]*"\|"status":"[A-Za-z]*","version":[0-9]*' | paste -d' ' - - | sed 's/"id":"\([a-f0-9]*\)","customerName":"\([^"]*\)" "status":"\([A-Za-z]*\)","version":\([0-9]*\)/\1  \3 (v\4)  \2/' ;;
  start)   move "${2:?order id}" Started ;;
  ready)   move "${2:?order id}" ReadyForPickup ;;
  deliver) move "${2:?order id}" Delivered ;;
  *) sed -n '2,13p' "$0"; exit 1 ;;
esac
