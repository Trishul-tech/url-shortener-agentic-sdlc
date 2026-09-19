# URL Shortener + Agentic SDLC Orchestrator

A URL shortener service (core APIs, analytics, reliability features) plus an
agentic orchestration layer that coordinates its own SDLC — requirements,
architecture, implementation, testing, documentation, and release readiness —
across explicit dependency graphs with governance, retries, rollback, and
audit-grade traceability.

This repo has two things in it, on purpose:

1. **`src/UrlShortener.*`** — the URL shortener itself: a clean-architecture
   .NET 8 Web API (Domain / Application / Infrastructure / Api) with SQLite,
   caching, rate limiting, and analytics.
2. **`src/UrlShortener.Orchestrator`** — the agentic orchestration engine
   that *built and validates changes to* the URL shortener. It's a
   standalone console app with its own dependency graph, retry/rollback
   logic, policy guardrails, human-approval checkpoints, and metrics. This
   is the assignment's "critical differentiator" and is where most of the
   engineering effort went.

See `docs/architecture.md` for how the two relate, and `docs/scenarios/` for
the three required scenarios (greenfield, brownfield, ambiguous) walked
through in detail.

## A note on how this was built

This solution was authored and reviewed by hand, with AI assistance, in a
sandboxed environment that had **no outbound access to install the .NET
SDK or NuGet packages** (its network egress allowlist covers only a couple
of package registries and GitHub, and the SDK download itself was blocked).
Every file here was written to compile and behave correctly by inspection,
cross-checked line-by-line against known-correct .NET 8 / EF Core 8 /
ASP.NET Core minimal API / xUnit APIs, and the control flow of the
orchestration engine was hand-traced through each of the three scenarios to
confirm the expected sequence of retries, rollbacks, and approvals — but
none of it has been run through `dotnet build` or `dotnet test` yet. Treat
the **first `dotnet build` locally as part of code review**, not a
formality. If you hit a compiler error, it's a real bug in this submission,
not a tooling problem — please tell me and I'll fix it immediately.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- No external services required — the API uses a local SQLite file, and the
  cache is in-process. Nothing to provision.

## Setup & run

```bash
# from the repo root
dotnet restore
dotnet build
```

### Run the API

```bash
dotnet run --project src/UrlShortener.Api
```

The API listens on the URL printed in the console (typically
`https://localhost:5001` / `http://localhost:5000` in Development). Swagger
UI is available at `/swagger` in the Development environment. The SQLite
database file is created automatically on first run (`urlshortener.dev.db`
in Development, `urlshortener.db` otherwise) — no migration step needed for
the prototype (see `docs/architecture.md` § Data & Migrations for the
production path).

Try it:

```bash
# create a short URL
curl -X POST http://localhost:5000/api/v1/urls \
  -H "Content-Type: application/json" \
  -d '{"targetUrl": "https://example.com/some/long/path"}'
# => { "code": "aB3xQ9z", "shortUrl": "https://short.link/aB3xQ9z", ... }

# follow it (302 redirect to the target)
curl -i http://localhost:5000/aB3xQ9z

# check analytics
curl http://localhost:5000/api/v1/urls/aB3xQ9z/analytics
```

### Run the tests

```bash
dotnet test
```

This runs unit tests (Domain, Application), integration tests (Api, against
an in-memory `TestServer` + throwaway SQLite file), and the orchestrator's
own engine + scenario tests.

### Run the agentic orchestrator

```bash
dotnet run --project src/UrlShortener.Orchestrator
```

This executes all three required scenarios (greenfield, brownfield,
ambiguous) back to back against the real engine, prints a summary to the
console, and writes the full audit trail, decision lineage, final stage
states, and reliability metrics for each run to `artifacts/sample-runs/`
(one set of four JSON files per scenario, e.g. `greenfield.audit.jsonl`,
`greenfield.metrics.json`, `greenfield.decision-lineage.json`,
`greenfield.stage-states.json`). Re-run it any time to regenerate them —
the scenarios are deterministic (scripted approvals, scripted failure
injection), so the output is stable across runs.

To run interactively (a real human types y/n at each approval checkpoint
instead of the scripted decisions), see `Governance/ApprovalGate.cs` -
`ConsoleApprovalProvider` is wired up but not the default; swap it in for
the scripted provider in a scenario's `Build()` method to try it.

## Project layout

```
src/
  UrlShortener.Domain          - entities, value objects, domain exceptions, repository interfaces
  UrlShortener.Application     - CQRS commands/queries (MediatR), validation, abstractions
  UrlShortener.Infrastructure  - EF Core + SQLite, caching, code generation
  UrlShortener.Api             - minimal API endpoints, middleware, Program.cs
  UrlShortener.Orchestrator    - the agentic SDLC orchestration engine + 3 scenarios
tests/
  UrlShortener.Domain.Tests
  UrlShortener.Application.Tests
  UrlShortener.Api.IntegrationTests
  UrlShortener.Orchestrator.Tests
docs/
  architecture.md                       - components, orchestration model, control flow, key decisions
  engineering-summary.md                - plan/rationale, artifacts, risks, assumptions, limitations
  testing-limitations-tradeoffs.md      - testing approach, known limitations, trade-offs
  scenarios/
    greenfield.md
    brownfield.md
    ambiguous.md
artifacts/sample-runs/                  - generated by `dotnet run --project src/UrlShortener.Orchestrator`
```

## API surface

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/v1/urls` | Create a short URL (optional custom alias, expiry) |
| `GET` | `/{code}` | Redirect (302) to the target URL |
| `DELETE` | `/api/v1/urls/{code}` | Deactivate a short URL |
| `GET` | `/api/v1/urls/{code}/analytics` | Click analytics for a date range (default: last 30 days) |
| `GET` | `/health` | Liveness/readiness check |

Full request/response contracts are in `src/UrlShortener.Api/Contracts` and
documented via Swagger at runtime.
