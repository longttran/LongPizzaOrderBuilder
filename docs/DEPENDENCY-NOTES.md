# Dependency notes: what `npm ci` reports, and what we decided

Running `npm ci` in `src/frontend` prints four kinds of message. None blocks the build or the tests, and none affects what ships to browsers.
This page says what each one means and what we chose to do. Versions are pinned exactly in `package.json`, so a change is always a deliberate commit.

| Message | What it means | Decision |
|---|---|---|
| `deprecated whatwg-encoding@3.1.1` | A small library pulled in by `jsdom` (the test environment), not by the app. Its maintainers suggest `@exodus/bytes`. | **No action.** It clears when `jsdom` stops using it. |
| `deprecated eslint@9.39.5: no longer supported` | ESLint is a direct dev dependency. The 9.x line is out of support; lint still runs. | **Upgrade to ESLint 10 later**, once the TypeScript and React-hooks plugins we use are confirmed to work with it. Tracked in [FUTURE-WORK.md](FUTURE-WORK.md). |
| `4 vulnerabilities (1 moderate, 1 high, 2 critical)` | See below. All four are in development tooling. | **Documented, not changed now.** The safe first step is described below. |
| `1 package has install scripts not yet covered by allowScripts: esbuild@0.25.12` | A newer npm asks you to decide which packages may run install scripts. `esbuild` (a build tool used by Vite) has a `postinstall`. | **Leave it.** See below. |

## The four vulnerabilities

`npm audit` lists these (checked on 2026-10-28):

| Package | Severity | Direct? | What the advisory says (its title) |
|---|---|---|---|
| `vite` | high | yes | The dev server may serve files whose names start like the public directory's; `server.fs` settings were not applied to HTML files |
| `vitest` | critical | yes | Arbitrary file read and execution while Vitest's **UI server** is listening |
| `@vitest/mocker` | moderate | no | Path traversal and code injection through mock registration |
| `tinypool` | critical | no | Prototype pollution in worker options leading to code execution |

- **Production is unaffected.** `npm audit --omit=dev` reports **0** vulnerabilities. The only runtime dependencies are `react` and `react-dom`. The production image serves static files from Nginx; Vite and Vitest are not in it.
- **Practical risk is low, but not zero.** We never run `vitest --ui` (`npm test` is `vitest run`), and the test environment is `jsdom`. We have **not** analysed the advisories beyond their titles. The `vite` finding concerns the dev server, and the **development** Compose file runs it on all interfaces and publishes port 5173, so use that setup only on a network you trust.
- **Do not run `npm audit fix --force`.** It jumps straight to Vitest 5.

### The fix path (tested in a scratch copy, not applied)

1. **`vite` 6.3.5 to 6.4.5** is a minor update within the same major. It clears the `vite` finding. With it, typecheck, lint, all 77 tests and the production build still pass.
2. **`vitest` 3.2.4 to 5.x** is what clears the other three. It is a major upgrade (it needs Node 22.12 or newer and Vite 6.4 or newer), so do it as its own change and let the test suite decide. npm's own "non-major fix" hint for Vitest (3.2.7) does **not** clear them.

## The `esbuild` install-scripts warning

`esbuild`'s `postinstall` checks and optimises the platform binary. esbuild ships that binary as a separate package, so we believe it works without the script; we have **not** verified that on your npm version.
If `npm run dev` or `npm run build` ever fails with a message about the esbuild binary, run the command npm itself printed: `npm install-scripts approve esbuild`. If npm later starts enforcing the policy, record that approval in the repo at that point.

## Useful commands

```bash
npm audit                 # all dependencies
npm audit --omit=dev      # only what ships to browsers (should say 0)
npm ls whatwg-encoding    # who pulls a package in
npm install-scripts ls    # which packages have install scripts
```

When upgrading a dependency: change one thing at a time, pin the exact version, then run `npm run typecheck`, `npm run lint`, `npm test`, `npm run build` and `npm audit`.
