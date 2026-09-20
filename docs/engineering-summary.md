# Final Engineering Summary

## Plan & rationale

The assignment asks for two things at once: a working URL shortener, and an
agentic system that demonstrates real SDLC orchestration - not a linear
task chain, but a governed, stateful, non-linear pipeline. Rather than
treat the orchestrator as a wrapper script that calls an AI to write code,
it's built as its own engineering artifact: an explicit dependency graph,
a scheduling engine, governance primitives (approval gates, policy
guardrails), reliability primitives (retry, rollback, re-plan, safe-stop),
and observability (audit log, decision lineage, metrics) - all independent
of any specific LLM, and all independently testable.

The build order followed the dependency direction of the product itself:
Domain → Application → Infrastructure → Api, then the orchestrator (which
has no runtime dependency on the product, only a narrative one via its
scenarios), then tests, then documentation. This is also the order a
reviewer should read the code in if reading top-to-bottom.

## Artifacts produced

- **Working prototype**: `src/UrlShortener.{Domain,Application,Infrastructure,Api}` -
  a runnable .NET 8 minimal-API service (create/redirect/deactivate/analytics)
  over SQLite, with caching, rate limiting, validation, and structured
  logging.
- **Agentic orchestration engine**: `src/UrlShortener.Orchestrator` -
  dependency graph + execution engine + agents + governance (approval
  gates, policy guardrails) + observability (audit log, decision lineage,
  reliability metrics), plus three runnable, deterministic scenarios.
- **Tests**: 4 test projects (`tests/*`) covering domain invariants,
  application handler logic, full HTTP integration, and the orchestration
  engine both at the mechanism level and the scenario level.
- **Documentation**: this file, `docs/architecture.md` (components,
  orchestration model, control flow, key decisions with alternatives
  considered), `docs/scenarios/{greenfield,brownfield,ambiguous}.md` (each
  showing decomposition, orchestration, and validation as required), and
  `docs/testing-limitations-tradeoffs.md`.
- **Generated run artifacts** (produced by `dotnet run --project src/UrlShortener.Orchestrator`,
  not checked in pre-generated - see below): per-scenario audit trail
  (JSON Lines), decision lineage (JSON), final stage states (JSON), and
  reliability metrics (JSON) in `artifacts/sample-runs/`.

## Risks, trade-offs, and validation

Covered in full, with specifics, in `docs/architecture.md` (§ decision
tables, § scale limitations) and `docs/testing-limitations-tradeoffs.md`
(testing approach, known limitations, explicit trade-off table). The
short version: the product is deliberately small and single-instance-scoped
so that engineering effort went into the orchestration engine, which is
what the assignment weights most heavily; every non-obvious decision (code
generation strategy, IP hashing, cache-vs-consistency trade-offs in both
the product and the brownfield scenario's proposed fix, agent
simulation vs. live LLM calls, round-based scheduling) is written down with
the alternative considered and why it was rejected, not just asserted.

## Assumptions

- "AI assistance (Copilot/Claude/etc.)" in the assignment describes how
  this repository was built (see root `README.md`), not a requirement that
  the orchestrator's own demo agents make live model calls at runtime to
  be a valid demonstration of the orchestration model - see
  `docs/architecture.md` §3.8 for the full reasoning and where a real
  model call would plug in (`IAgent`).
- A take-home reviewer has no external services provisioned and no cloud
  credentials, so the prototype was scoped to run entirely locally
  (SQLite, in-memory cache, no auth provider) rather than assume a
  particular cloud stack.
- "Reliability metrics" (success rate, retry/rollback frequency, MTTR,
  end-to-end latency) means per-pipeline-run metrics collected by the
  orchestrator itself, not metrics about the deployed URL shortener's
  production traffic (which would be a different, ops-facing concern -
  e.g. Application Insights / Prometheus on the API - out of scope for a
  2-3 day prototype).

## Limitations

The full, itemized list (compile status, the plausibly-racy handler the
brownfield scenario's narrative is modeled on, single-instance scale,
auth, scripted-vs-production governance, scheduling model) is in
`docs/testing-limitations-tradeoffs.md` and is written to be read, not
skimmed past - in particular item 1 (how this solution was authored with AI assistance and then actually compiled, tested, and debugged on a real machine - including the four real bugs that surfaced and were fixed) and item 2 (the honest relationship between the brownfield
scenario's narrative and this repo's actual current code) are the two
most important caveats for a reviewer to internalize before judging
correctness.
