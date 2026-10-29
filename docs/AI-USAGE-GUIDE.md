# Guide: describing AI usage in this project

The brief asks for **a few sentences** covering three things. This guide helps you write them in your own voice. It is a guide, not the note: the note should say only what you did and saw yourself.

1. **Which AI tools you used, and for what.**
2. **One example of AI output you rejected or had to fix.**
3. **One behaviour or invariant you verified yourself rather than trusting the AI, and how.**

## 1. The facts about tools (from this project's history)

| Tool | Used for |
|---|---|
| **Claude (Sonnet 5.5)** in the Claude app. Add which client you used, for example web, desktop or mobile | Wrote nearly all of the code, tests and documents, and reviewed the repository |
| A Linux sandbox that Claude ran commands in: `bash`, `git`, `curl`, Python | Cloning, committing, producing patches, calling the APIs, scripted edits |
| .NET 10 SDK (inside the sandbox) | Compiling the real C# and running both back ends as processes |
| Node 22 with Vite, TypeScript, Vitest, Testing Library, jsdom and ESLint (inside the sandbox) | Building, type-checking, linting and testing the React page |
| Playwright with Chromium (inside the sandbox) | Driving the real page against the real services in a browser; screenshots |
| **Stand-ins for xunit and `WebApplicationFactory`** that Claude wrote | The sandbox could not download NuGet packages, so the backend tests were first run through these. They are **not** the real xunit |

Not used: web search. **Run by you, not by the AI:** `dotnet build` and `dotnet test` on Windows with the real xunit and its analyzers, `docker compose up`, `npm ci`, and (once opened) the CI run on your pull request. Say which of those you ran.

## 2. How to write each part

**Tools.** One or two sentences: the tool, and the fact that it wrote most of the code, tests and documents and ran them. Be direct. "Helped with" understates it.

**An example you rejected or had to fix.** Choose one you actually saw or reviewed. Say what was wrong, how it was found, and what changed. Candidates (all real; pick one you can speak to):

| What went wrong | How it was found | Where to see it |
|---|---|---|
| The first race tests relied on thread timing. On a one-core machine they still passed with the lock deleted from the store, so they proved nothing. They were replaced with tests that freeze one request half-way through its change | The AI deliberately removed the lock and saw no test fail | `tests/…UnitTests/PausingClock.cs`; tests named `While_…half_way…` in `OrderLifecycleTests.cs` |
| A UI test claimed "no price arithmetic in the browser" but never checked the tax amount | The AI broke the code on purpose (computed tax in the browser) and the tests stayed green | `src/frontend/src/App.test.tsx`, the `UI-001` tests |
| The pricing hook trusted a cancelled request to fail, so a late reply could overwrite a newer price | A unit test with a mocked call failed | the comment in `src/frontend/src/hooks/usePricingPreview.ts` |
| **The first real build failed**: four `CS0433` errors (two types named `Program`) and 41 xunit analyzer warnings, which the AI's own checks had not caught | **You** ran `dotnet build` on Windows | `docs/HISTORY.md`, "Fixes from the first real build"; the comment in `OrderService/Program.cs` |
| The AI's fix for those warnings introduced one more (`xUnit2031`) | **You** ran `dotnet build` again | the `Assert.Single(created, …)` line in `OrderIdempotencyTests.cs` |
| Several overlapping patch files left your branch a mixture of versions (an old test file, the update feature code, an unfixed entry point) | **You** hit the build errors | the branch history |

Items marked **You** are ones you found yourself, which makes them the easiest to write about truthfully.

**Something you verified yourself.** This must be your own check. Do it, then write what you saw. Options, with exact steps:

| Behaviour | What to do | What you should see |
|---|---|---|
| Cancel versus restaurant start has exactly one winner | `scripts/dev.sh`, then `scripts/demo-order-service.sh`; read section 2 of the output | Every round shows one `200` and one `409`, and a history with only the winning move |
| A lock really protects it | In `OrderStore.cs`, delete `lock (_gate)` in `Transition`, run `dotnet test`, then `git checkout` the file | The two tests named `While_…half_way…` fail |
| A retry makes one order | `POST /api/orders` twice with the same `Idempotency-Key`, then `scripts/restaurant.sh list` | One order with that name, and the second reply says `Idempotency-Replayed: true` |
| The price is right | Work one order out by hand (the README's example: 2 × (12.00 + 0.96 + 1.32) = 28.56, minus 5.00, tax 6.5%, tip 18%) | The service returns 29.61 |
| The page tells the truth when the restaurant starts first | Place an order, run `restaurant.sh start <full id>`, and press **Cancel this order** before the 5-second refresh | A refusal with the reason, and the page shows "Started" |

Write it as: what you did, what you expected, what happened, and roughly when.

## 3. A skeleton to fill in

> I used Claude (Sonnet 5.5, in the Claude [client]) to write the code, tests and documentation, and to build, run and test them in its own sandbox.
> **One thing I had to fix:** [what was wrong, how I found it, what changed].
> **One thing I checked myself rather than trusting it:** [the behaviour, what I did, what I saw].
> I ran [what you ran: for example `dotnet test`, `docker compose up`, the CI run] myself; [anything you did not run].

## 4. Do and don't

- **Do** say plainly that the AI wrote most of the work, and what you reviewed and ran. Dates and file names make a claim checkable.
- **Do** mention the limits that are true: the sandbox used stand-ins for xunit, and it could not run Docker or CI.
- **Don't** claim you tested something you did not, or copy a result from this guide as if it were yours.
- **Don't** make it long. A reader should be able to check each sentence against the repository.

Put the finished note where the reader will look, for example a short section near the end of the README.
