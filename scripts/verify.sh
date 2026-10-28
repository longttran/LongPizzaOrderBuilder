#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

echo '== .NET unit and API integration tests =='
dotnet test tests/LongPizzaOrderBuilder.Tests/LongPizzaOrderBuilder.Tests.csproj --configuration Release

echo '== Frontend dependency install and production build =='
(
  cd src/frontend
  npm install --no-audit --no-fund
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

echo '== Verify the menu API =='
curl --fail --silent http://localhost:8080/api/menu | python3 -m json.tool

echo
echo 'Verification passed. The development stack remains running.'
echo 'Frontend: http://localhost:5173'
echo 'API health: http://localhost:8080/health'
echo 'Stop the stack with: docker compose down'
