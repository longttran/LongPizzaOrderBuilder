$ErrorActionPreference = 'Stop'

Write-Host '== .NET unit and API integration tests ==' -ForegroundColor Cyan
dotnet test tests/LongPizzaOrderBuilder.UnitTests/LongPizzaOrderBuilder.UnitTests.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'unit tests failed.' }
dotnet test tests/LongPizzaOrderBuilder.IntegrationTests/LongPizzaOrderBuilder.IntegrationTests.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'integration tests failed.' }

Write-Host '== Invariant registry matches the tests ==' -ForegroundColor Cyan
node scripts/check-invariants.mjs
if ($LASTEXITCODE -ne 0) { throw 'invariant check failed.' }

Write-Host '== Frontend dependency install and production build ==' -ForegroundColor Cyan
Push-Location src/frontend
try {
    npm ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    npm run typecheck
    if ($LASTEXITCODE -ne 0) { throw 'typecheck failed.' }
    npm run lint
    if ($LASTEXITCODE -ne 0) { throw 'lint failed.' }
    npm test
    if ($LASTEXITCODE -ne 0) { throw 'frontend tests failed.' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'npm run build failed.' }
}
finally {
    Pop-Location
}

Write-Host '== Build and start the Docker Compose stack ==' -ForegroundColor Cyan
docker compose up --build -d
if ($LASTEXITCODE -ne 0) { throw 'docker compose up --build failed.' }

Write-Host 'Waiting for the API health endpoint...'
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    try {
        $health = Invoke-RestMethod -Uri 'http://localhost:8080/health' -TimeoutSec 3
        $ready = $true
        break
    }
    catch {
        Start-Sleep -Seconds 2
    }
}
if (-not $ready) {
    docker compose logs api
    throw 'API did not become healthy. Inspect docker compose logs.'
}

Write-Host '== Verify the menu API ==' -ForegroundColor Cyan
$menu = Invoke-RestMethod -Uri 'http://localhost:8080/api/menu' -TimeoutSec 10
$menu | ConvertTo-Json -Depth 8
Write-Host ''
Write-Host 'Verification passed. The development stack remains running.' -ForegroundColor Green
Write-Host 'Frontend: http://localhost:5173'
Write-Host 'API health: http://localhost:8080/health'
Write-Host 'Stop the stack with: docker compose down'
