# Usage audit: what is in the repository and whether it is used

A check of everything tracked in the repository, done on 2026-10-28 against the pull request branch. "Used" means the app, its tests, CI, Compose or a documented command depends on it.
Two things were removed (the placeholder `worker` and dead CSS); everything else was confirmed as used. Git history keeps the removed files if they are ever wanted again.

| Area | How it was checked | Result |
|---|---|---|
| Projects in the solution | Project references, the `.slnx`, Compose and CI | `Pricing`, `Api`, `OrderService` and the two test projects are used. **`Worker` removed:** nothing depended on it, and it only logged a heartbeat |
| Compose services | Which services the others depend on, and what each runs | `api`, `orderservice` and `frontend` are used. `postgres` and `rabbitmq` (removed earlier) and `worker` (removed now) were not |
| Dockerfiles and Nginx | Whether Compose or CI uses each | `api`, `orderservice` and `frontend` Dockerfiles and `deploy/nginx/frontend.conf` are used. `worker.Dockerfile` removed |
| NuGet packages | Each `PackageReference` against the code | All used, except the worker's `Microsoft.Extensions.Hosting`, removed with it. The Api project also pins `Microsoft.OpenApi` explicitly; that was **not** verified (no package restore was possible) and was kept |
| npm packages | `depcheck` | It lists `jsdom`, `vite`, `eslint` and `@testing-library/dom` as unused. All four are needed: `jsdom` is the Vitest environment (`vite.config.ts`), `vite` and `eslint` run from the `package.json` scripts, and `@testing-library/dom` is a required peer of `@testing-library/react`. **None removed** |
| C# production code | Every type and public member, looking for names referenced nowhere else | Nothing unused, apart from controllers and entry points that the framework finds itself |
| TypeScript code | Every export and every file | Nothing unused (`test/setup.ts` is loaded by the Vitest config) |
| CSS | Every class name against the page's markup | **26 unused rules removed:** 25 in `style.css` (leftovers from the original prototype's hero artwork and similar) and 1 in `components.css`. `style.css` shrank from 10,076 to 7,370 bytes. The page was screenshotted in 5 states at desktop and mobile widths before and after, and all 10 images are byte-for-byte identical |
| Scripts | Whether the README, CI or another script uses each | All used. `verify.ps1` is the PowerShell twin of `verify.sh` |
| Config and support files | Whether a tool or document uses each | All used (`.dockerignore` and `LICENSE` by Docker and GitHub directly, `CLAUDE.md` by Claude Code, `.vscode/settings.json` by the editor) |
| Documents | Whether each is linked from the README or an index | All linked. `PLANNING.md` and `OPERATIONS.md` held passages from before the order service; they were then **pruned** (see `HISTORY.md`): `PLANNING.md` became `BUSINESS-RULES.md`, and each file ends with a "Previous iterations" note |
| Empty folders | Tracked files in every directory | **None in the repository.** Git does not track empty folders at all, so any you see are local leftovers (see below) |

## Empty folders on your machine

Switching branches or applying a patch with Git removes folders it has emptied, so these are usually leftovers from build output that was cleaned or from an editor. They are not part of the repository and deleting them changes nothing in it. In PowerShell, from the repo folder:

```powershell
# 1. List empty folders (skips .git, node_modules, bin and obj)
Get-ChildItem -Directory -Recurse -Force |
  Where-Object { $_.FullName -notmatch '\\(\.git|node_modules|bin|obj)(\\|$)' -and -not (Get-ChildItem $_.FullName -Force) } |
  Select-Object -ExpandProperty FullName

# 2. See what Git is ignoring (build output, node_modules): a dry run, nothing is deleted
git clean -ndX

# 3. Delete the empty folders you listed in step 1 (review the list first), for example:
#    Remove-Item "C:\path\to\empty\folder"
```

These PowerShell commands have not been run (PowerShell is not available where this was written), so check the list before deleting anything.
