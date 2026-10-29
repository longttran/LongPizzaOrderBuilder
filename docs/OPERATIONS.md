# Setup, local development and deployment (detailed)

> The short version (run everything, run the tests) is in the [README](../README.md). This page has the detail: prerequisites, the .NET SDK, running the tests, editor conventions, self-hosting and cloud notes.
> The page, the builder API (port 8080) and the order service (port 8090) all start from `compose.yaml` or `scripts/dev.sh`. Earlier material (the first-commit walkthrough and the initial smoke-test status) is summarised in [Previous iterations](#previous-iterations) at the end.

## Local development

### Prerequisites

- Docker Desktop or Docker Engine with the Docker Compose plugin.
- For backend development/tests outside containers: .NET 10 SDK (required; .NET SDK 8 cannot build these `net10.0` projects) and Node.js 22. Docker builds use .NET 10 images and are independent of the host SDK version.

From the repository root:

```bash
# Optional: cp .env.example .env   (Windows PowerShell: Copy-Item .env.example .env). Compose runs without a .env file.
docker compose up --build
```

Development endpoints:

- React/Vite: <http://localhost:5173>
- API: <http://localhost:8080>
- Menu: <http://localhost:8080/api/menu>
- Pricing preview: `POST http://localhost:8080/api/pricing/preview`
- Order service: <http://localhost:8090> (orders at `/api/orders`, the restaurant's side at `/api/restaurant/orders`)
- Order service health: <http://localhost:8090/health>
- API health: <http://localhost:8080/health>
- OpenAPI in development: <http://localhost:8080/openapi/v1.json>

There is no database or message broker: RabbitMQ and PostgreSQL were removed from Compose because no code used them (see [FUTURE-WORK.md](FUTURE-WORK.md)). If you ran an earlier version of the stack, run `docker compose up --build --remove-orphans` once to clear the old `postgres` and `rabbitmq` containers; their leftover volumes can be listed with `docker volume ls` and deleted with `docker volume rm`.

#### Verify the .NET SDK

The API, order service, pricing library, and test projects target **.NET 10**. Install the .NET 10 SDK from the [official .NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) if it is not already installed. On Windows, WinGet is also available:

```powershell
winget install Microsoft.DotNet.SDK.10
```

Restart VS Code and verify the selected SDK:

```powershell
dotnet --list-sdks
dotnet --version
```

The repository's `global.json` selects the .NET 10 SDK family. SDKs can be installed side by side. See Microsoft's [Windows installation guide](https://learn.microsoft.com/en-us/dotnet/core/install/windows) if SDK selection or PATH detection fails.

#### Run backend tests

Run these commands from the repository root. The solution includes the API, order service, pricing library, and the separate unit and integration test projects:

```powershell
dotnet restore .\LongPizzaOrderBuilder.slnx
dotnet build .\LongPizzaOrderBuilder.slnx --configuration Release --no-restore
dotnet test .\LongPizzaOrderBuilder.slnx --configuration Release --no-build --no-restore
```

For a quick test run, run the projects individually:

```powershell
dotnet test .\tests\LongPizzaOrderBuilder.UnitTests\LongPizzaOrderBuilder.UnitTests.csproj
dotnet test .\tests\LongPizzaOrderBuilder.IntegrationTests\LongPizzaOrderBuilder.IntegrationTests.csproj
```

Build the frontend outside Docker:

```bash
cd src/frontend
npm ci --no-audit --no-fund
npm run build
```

### Editor formatting and linting conventions

The repository includes a root `.editorconfig` so supported editors agree on whitespace and indentation. Use spaces rather than hard tabs: **4 spaces for C#**, **2 spaces for TypeScript/TSX, JSON, YAML, and CSS**, and no trailing whitespace. Keep a final newline in text files. These are editor conventions; they do not automatically reformat every existing file.

For VS Code:

- Install the Microsoft **C# Dev Kit** extension and keep VS Code/the extension current for .NET 10 support.
- Enable **Editor: Format On Save** if desired. C# formatting is provided by the C# tooling; `.editorconfig` provides the shared rules.
- For the frontend, **ESLint** is configured and enforced (`npm run lint`, and in CI). **Prettier** is not a dependency; if you use it in your editor, do not reformat files you are not otherwise changing.
- If VS Code does not pick up `.editorconfig`, run **Developer: Reload Window** and check that the relevant language formatter extension is enabled.

Frontend tests and the type check run with `npm test` and `npm run typecheck`; see the [README](../README.md).

### Self-host with production-style containers

The `compose.selfhost.yaml` profile builds a static frontend image served by Nginx, an ASP.NET Core API image, and an order service image. Nginx exposes the app on one host port and proxies `/api` to the API over the private Compose network. The restaurant routes are not published.

1. Optional: copy `.env.example` to `.env` to change a setting. There are no passwords now. Keep `.env` out of source control.
2. Start the stack:

   ```bash
   docker compose -f compose.selfhost.yaml up --build -d
   ```

3. Open <http://localhost:8088> (or the port configured by `HTTP_PORT`). Check `docker compose -f compose.selfhost.yaml ps` and `docker compose -f compose.selfhost.yaml logs -f api orderservice frontend` for health and startup errors.
4. Stop the stack with `docker compose -f compose.selfhost.yaml down`. There are no data volumes now: orders are held in memory and are lost when the order service stops.

Self-hosting notes:

- Expose only the frontend/reverse-proxy port to the LAN or internet. Do not publish the order service's restaurant routes (`/api/restaurant`); they have no authentication.
- Put TLS in front of the app before exposing it to the internet; configure firewall rules, host patching, secret rotation, and tested backups.
- There is no database or message broker. Orders live in the order service's memory, so a restart loses them.
- The self-host profile is therefore suitable for verifying container wiring and a demo, not yet for handling real customer orders.

### Cloud portability

Keep application images provider-neutral and configuration in environment variables/secrets. Once a cloud is selected, choose a managed container runtime and, once persistence is built, a managed database; add registry publishing, secret integration, TLS/domain configuration, backups, monitoring, and deployment/rollback automation. Do not commit to Azure, AWS, or Google Cloud-specific services until the deployment requirements and budget are clearer.

## What belongs in the repository

Source code, tests, deployment and configuration files, scripts, documents and CI workflows. Not generated or local files. The root `.gitignore` already excludes `.env`, .NET `bin/` and `obj/`, test results, `node_modules/`, frontend `dist/`, coverage output, IDE state and OS metadata.

- Commit: `src/`, `tests/`, `deploy/`, `scripts/`, `docs/`, the Compose files, `.github/workflows/`, `.env.example`, `.editorconfig`, `.gitattributes`, `global.json`, `.vscode/settings.json` (shared workspace settings only) and `src/frontend/package-lock.json`.
- Do not commit: `.env` or any secret, build output, `node_modules/`, logs, patch files, or ZIP archives of the project.
- Before committing, review `git status` and `git diff --cached`.

## Previous iterations

Removed from this page because they no longer apply; the full text is in Git history (`git log --follow docs/OPERATIONS.md`).

| Earlier content | Why it was removed |
|---|---|
| A step-by-step first-commit and first-push walkthrough, with a "first upload" checklist | The repository exists and has history. The short list above covers what still matters |
| A note that the local Debug test run passed 17 tests | Out of date: the suites are far larger and are described in [DESIGN.md](DESIGN.md) section 7 |
| A note that ESLint and Prettier were not declared and linting was not enforced | ESLint is now declared, runs through `npm run lint`, and runs in CI |
| Endpoints and service credentials for PostgreSQL and RabbitMQ | Both were removed from Compose because nothing used them ([ADR-0010](decisions/ADR-0010-decisions-outside-the-scope.md)) |
