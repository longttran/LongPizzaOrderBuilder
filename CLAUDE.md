@AGENTS.md

## Claude Code specifics

- Before changing pricing or cart behaviour, read `docs/INVARIANTS.md` and the ADR it points to. State which invariant IDs your
  change touches.
- Finish a task by running the commands in AGENTS.md and reporting actual results. If a tool is unavailable (for example no
  .NET SDK, or NuGet unreachable), say so and say what was not run.
- When asked to add a rule, add it to `docs/INVARIANTS.md` first as `planned`, write the failing test, then make it pass and
  flip it to `enforced`.
- Prefer small, reviewable diffs. Do not reformat files you are not otherwise changing (`src/frontend/src/style.css` is
  minified; leave it alone and put new styles in `components.css`).
- Do not edit `package-lock.json` by hand; use `npm install --save-exact` and commit the result.
