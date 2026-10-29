#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

echo '== .NET unit and API integration tests =='
dotnet test tests/LongPizzaOrderBuilder.UnitTests/LongPizzaOrderBuilder.UnitTests.csproj --configuration Release
dotnet test tests/LongPizzaOrderBuilder.IntegrationTests/LongPizzaOrderBuilder.IntegrationTests.csproj --configuration Release

echo '== Invariant registry matches the tests =='
node scripts/check-invariants.mjs

echo '== Frontend dependency install and production build =='
(
  cd src/frontend
  npm ci --no-audit --no-fund
  npm run typecheck
  npm run lint
  npm test
  npm run build
)

echo '== Build and start the Docker Compose stack =='
docker compose up --build -d

ready=0
for _ in $(seq 1 30); do
  if curl --fail --silent http://localhost:8080/health >/dev/null; then
    ready=1
    break
  fi
  sleep 2
done
if [[ "$ready" -ne 1 ]]; then
  docker compose logs api
  echo 'API did not become healthy. Inspect docker compose logs.' >&2
  exit 1
fi

echo '== Verify the menu API (order builder) =='
curl --fail --silent http://localhost:8080/api/menu | python3 -m json.tool

echo '== Verify the order service =='
curl --fail --silent http://localhost:8090/health >/dev/null && echo 'order service healthy'

echo
echo 'Verification passed. The development stack remains running.'
echo 'Frontend: http://localhost:5173'
echo 'API health: http://localhost:8080/health'
echo 'Order service: http://localhost:8090/health  (try scripts/demo-order-service.sh)'
echo 'Stop the stack with: docker compose down'
