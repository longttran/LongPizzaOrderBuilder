# Setup, local development and deployment (detailed)

> Moved from the README. The short version (run both processes, run the tests) is in the [README](../README.md). Parts of this
> predate the separate order service: where a step mentions only the API on port 8080, the order service now also runs on port 8090
> (see `compose.yaml` and `scripts/dev.sh`).

## Local development

### Prerequisites

- Docker Desktop or Docker Engine with the Docker Compose plugin.
- For backend development/tests outside containers: .NET 10 SDK (required; .NET SDK 8 cannot build these `net10.0` projects) and Node.js 22. Docker builds use .NET 10 images and are independent of the host SDK version.

From the repository root:

```bash
cp .env.example .env
# Windows PowerShell: Copy-Item .env.example .env
docker compose up --build
```

Development endpoints:

- React/Vite: <http://localhost:5173>
- API: <http://localhost:8080>
- Menu: <http://localhost:8080/api/menu>
- Pricing preview: `POST http://localhost:8080/api/pricing/preview`
- Orders: <http://localhost:8080/api/orders>
- API health: <http://localhost:8080/health>
- OpenAPI in development: <http://localhost:8080/openapi/v1.json>
- RabbitMQ management: <http://localhost:15672>
- PostgreSQL: `localhost:5432`

#### Verify the .NET SDK

The API, Worker, and test projects target **.NET 10**. Install the .NET 10 SDK from the [official .NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) if it is not already installed. On Windows, WinGet is also available:

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

Run these commands from the repository root. The solution includes the API, Worker, and the separate unit and integration test projects:

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

The local Debug solution test run passed **17 tests (0 failed, 0 skipped)**. Re-run the Release commands above before treating Release configuration or CI as verified.

Build the frontend outside Docker:

```bash
cd src/frontend
npm install --no-audit --no-fund
npm run build
```

### Editor formatting and linting conventions

The repository includes a root `.editorconfig` so supported editors agree on whitespace and indentation. Use spaces rather than hard tabs: **4 spaces for C#**, **2 spaces for TypeScript/TSX, JSON, YAML, and CSS**, and no trailing whitespace. Keep a final newline in text files. These are editor conventions; they do not automatically reformat every existing file.

For VS Code:

- Install the Microsoft **C# Dev Kit** extension and keep VS Code/the extension current for .NET 10 support.
- Enable **Editor: Format On Save** if desired. C# formatting is provided by the C# tooling; `.editorconfig` provides the shared rules.
- For the frontend, use **Prettier** to format TypeScript/TSX, JSON, CSS, and Markdown. The repository does not currently declare Prettier or ESLint as dependencies, so frontend linting is not yet enforced by an npm script or CI. Add and pin those tools, add `format`, `format:check`, and `lint` scripts, and run them in CI before treating linting as a quality gate.
- If VS Code does not pick up `.editorconfig`, run **Developer: Reload Window** and check that the relevant language formatter extension is enabled.

Suggested frontend tools: [Prettier](https://prettier.io/) for consistent formatting and [ESLint](https://eslint.org/) with the TypeScript and React hooks plugins for code-quality rules. Avoid adding auto-fix rules that silently alter behavior without reviewing the diff.

### Self-host with production-style containers

The `compose.selfhost.yaml` profile builds a static frontend image served by Nginx, an ASP.NET Core API image, a worker image, PostgreSQL, and RabbitMQ. Nginx exposes the app on one host port and proxies `/api` to the API over the private Compose network. PostgreSQL and RabbitMQ do not publish host ports in this profile.

1. Copy `.env.example` to `.env` and replace **every** example password with a long, unique secret. Keep `.env` out of source control.
2. Start the stack:

   ```bash
   docker compose -f compose.selfhost.yaml up --build -d
   ```

3. Open <http://localhost:8088> (or the port configured by `HTTP_PORT`). Check `docker compose -f compose.selfhost.yaml ps` and `docker compose -f compose.selfhost.yaml logs -f api frontend worker` for health and startup errors.
4. Stop the stack with `docker compose -f compose.selfhost.yaml down`. This preserves named data volumes. To remove volumes as well, `docker compose -f compose.selfhost.yaml down -v` permanently deletes the local database/broker data.

Self-hosting notes:

- Expose only the frontend/reverse-proxy port to the LAN or internet. Do not publish PostgreSQL or RabbitMQ ports publicly.
- Put TLS in front of the app before exposing it to the internet; configure firewall rules, host patching, secret rotation, and tested backups.
- PostgreSQL and RabbitMQ containers are included as infrastructure, but **the current application does not yet persist orders to PostgreSQL or publish/consume RabbitMQ events**. The worker is a scaffold, not an order processor. The database volume alone does not make orders durable.
- The self-host profile is therefore suitable for verifying container wiring and a demo, not yet for handling real customer orders.

### Cloud portability

Keep application images provider-neutral and configuration in environment variables/secrets. Once a cloud is selected, choose a managed container runtime, managed PostgreSQL, and an appropriate managed queue or RabbitMQ hosting option; add registry publishing, secret integration, TLS/domain configuration, backups, monitoring, and deployment/rollback automation. Do not commit to Azure, AWS, or Google Cloud-specific services until the deployment requirements and budget are clearer.

## First Git repository upload

This repository is intended to contain **source code, tests, deployment/configuration files, developer instructions, and CI workflows**—not generated build output, local secrets, or runtime data.

### Files to include

Commit the project files that are part of the repository, including:

- `README.md`, `.gitignore`, `.gitattributes`, `.editorconfig`, `global.json`, and `.env.example`.
- `src/`, `tests/`, `deploy/`, `scripts/`, and the Compose files.
- `.github/workflows/` so CI runs on the remote repository.
- `.vscode/settings.json` for shared workspace formatting preferences (personal extensions and machine-specific settings should not be committed).
- `src/frontend/package-lock.json` **after generating it with a successful npm install**. Commit the lockfile so dependency versions are reproducible; do not commit `node_modules/`.

### Do not upload

The root `.gitignore` excludes common generated or local-only content, including `.env` and other environment-specific secret files, .NET `bin/` and `obj/`, test results, Node `node_modules/`, frontend `dist/`, coverage output, IDE state, and OS metadata. Keep `.env.example` as a template with placeholders only. Do not commit passwords, tokens, production connection strings, database dumps, RabbitMQ/PostgreSQL data volumes, logs, or exported artifacts.

Dockerfiles, Compose manifests, source files, tests, and CI configuration are **not artifacts**; they are required project inputs and should be committed. Do not commit ZIP archives of the project inside the repository.

### Prepare and inspect the first commit

Before staging, stop local services if desired and ensure `.env` contains only local settings. From the repository root in PowerShell:

```powershell
# Generate the frontend lockfile (requires network access to npm).
cd src/frontend
npm install --no-audit --no-fund
cd ../..

# Review what Git sees before staging anything.
git init
git status --short
git check-ignore -v .env src/backend/LongPizzaOrderBuilder.Api/obj src/frontend/node_modules

# Stage the project and inspect the exact upload set.
git add .
git status --short
git diff --cached --stat
git diff --cached --check
git diff --cached
```

**Do not commit until you have inspected the staged diff.** Confirm that `.env`, credentials, generated directories, local database data, and ZIP files are absent. If a secret is accidentally staged, unstage it and remove it from the index before committing; if it was already committed or pushed, rotate the secret because deleting it in a later commit does not erase history.

Once the staged files look correct, make the initial commit:

```powershell
git commit -m "Initial LongPizzaOrderBuilder project"
```

Create an empty repository on your Git hosting provider (for example, GitHub); do not initialize it with a second README or license if you already have those locally. Then add the provider's repository URL and push the initial branch using the commands shown by that provider. For GitHub, the usual shape is:

```powershell
git branch -M main
git remote add origin https://github.com/YOUR-ACCOUNT/LongPizzaOrderBuilder.git
git push -u origin main
```

Replace the example URL with your real repository URL. Review repository visibility and access before pushing; choose private if you do not intend the code and project history to be public.

### First-upload verification checklist

- [ ] `.env.example` contains placeholders only; `.env` is ignored.
- [ ] `.gitignore`, `.gitattributes`, `.editorconfig`, and `global.json` are present.
- [ ] `src/frontend/package-lock.json` is committed; `node_modules/` is not.
- [ ] Source, tests, Docker/Compose configuration, scripts, README, and CI workflow are included.
- [ ] No build output, test results, logs, database volumes/dumps, secrets, or project ZIP archives are staged.
- [ ] `git diff --cached --check` reports no whitespace errors.
- [ ] The first commit is created only after reviewing the staged diff.
- [ ] After pushing, the GitHub Actions workflow result is checked and recorded in this README.
