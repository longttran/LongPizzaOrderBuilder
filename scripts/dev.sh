#!/usr/bin/env bash
# Runs the whole thing without Docker: the order builder's API (:8080), the order service (:8090) and the web page (:5173).
# Needs the .NET 10 SDK and Node 22. Stop everything with Ctrl+C.
# Optional: RESTAURANT_AUTO_ADVANCE_SECONDS=30 scripts/dev.sh   makes a timer play the restaurant (otherwise use scripts/restaurant.sh).
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
trap 'kill 0' EXIT
ASPNETCORE_URLS=http://localhost:8080 dotnet run --project src/backend/LongPizzaOrderBuilder.Api &
ASPNETCORE_URLS=http://localhost:8090 Restaurant__AutoAdvanceSeconds="${RESTAURANT_AUTO_ADVANCE_SECONDS:-0}" dotnet run --project src/backend/LongPizzaOrderBuilder.OrderService &
(cd src/frontend && npm install --no-audit --no-fund && npm run dev) &
wait
