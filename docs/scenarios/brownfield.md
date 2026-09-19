# Scenario 2: Brownfield — Fix a Concurrency Bug in Click Analytics

**Code**: `src/UrlShortener.Orchestrator/Scenarios/BrownfieldScenario.cs`
**Run it**: `dotnet run --project src/UrlShortener.Orchestrator`
**Artifacts**: `artifacts/sample-runs/brownfield.*`

This is the primary showcase for the assignment's "critical
differentiator": codebase reasoning, a stage that fails past its retry
budget, an automatic rollback, dynamic re-planning of the affected
subgraph, and a clean recovery on rework - **without a human re-triggering
the pipeline**.

## The ask

> Fix: `ResolveShortUrlQuery` double-records a click when the in-memory
> cache entry is populated concurrently by two in-flight requests for the
> same code, and refactor the cache-populate path to remove the race.

A real bug against the real codebase built for this assignment
(`src/UrlShortener.Application/UrlShortening/ResolveShortUrl/ResolveShortUrlHandler.cs`
does read-then-populate the cache without coalescing concurrent misses -
under a burst of simultaneous first-requests for the same code, more than
one can miss the cache and each records its own click).

## 1. Requirement understanding

Unambiguous - a confirmed production bug with a clear scope ("no behavior
change to the public API contract"). Approved immediately by the scripted
`eng-lead`.

## 2. Codebase reasoning (architecture stage)

This is where brownfield work differs structurally from greenfield:
`ArchitectureAgent` doesn't propose something new, it explains the
**existing system's** behavior and where it breaks:

> Root cause: under a burst, two concurrent requests for the same code can
> both miss the cache before it is populated, so both fall through to the
> repository and both record a click. Fix: populate the cache and record
> the click as a single atomic step per request, keyed so a second
> concurrent miss reuses the first request's in-flight result instead of
> issuing its own repository read.

Impacted modules are named explicitly and match the real files in this
repo:

- `UrlShortener.Application/UrlShortening/ResolveShortUrl/ResolveShortUrlHandler.cs`
- `UrlShortener.Infrastructure/Services/MemoryCacheService.cs`
- `UrlShortener.Infrastructure/Persistence/Repositories/ClickEventRepository.cs` (read-only impact analysis - not modified, but its call pattern is what the fix changes)

A trade-off is recorded and approved: a distributed lock (e.g. Redis
`SETNX`) was considered and rejected as unnecessary complexity for the
current single-instance deployment; request-coalescing was chosen over
strict read-your-writes consistency specifically to keep the redirect hot
path low-latency (see `docs/architecture.md` § Scale limitations - this is
the same single-instance assumption made there).

## 3. Implementation → regression → retry → rollback → re-plan → recovery

This is the sequence the engine actually executes, traced step by step:

1. **Implementation runs (execution #1)**: produces the two touched files,
   succeeds structurally.
2. **UnitTesting, IntegrationTesting, SecurityReview, Documentation become
   ready together** (parallel branch, same as greenfield). UnitTesting,
   SecurityReview, and Documentation all pass immediately.
3. **IntegrationTesting fails**, twice (its `MaxRetries: 1` budget = 2
   attempts, both consumed): a scripted load test result -
   *"50 concurrent redirects for the same code recorded 63 click events
   (expected 50) - race still reproduces"* - because the fix hasn't
   actually been applied yet in this execution generation (the scenario
   deliberately models "the first implementation pass didn't fully close
   the race").
4. **Retry budget exhausted → rollback triggers.** Because
   `IntegrationTesting` has a configured rollback target
   (`OrchestrationOptions.RollbackTargets[IntegrationTesting] = Implementation`),
   the engine resets **Implementation and every stage transitively
   downstream of it** - `UnitTesting`, `IntegrationTesting`,
   `SecurityReview`, `Documentation`, even the ones that already passed in
   step 2 - back to `Pending`. This is the **dynamic re-planning**
   requirement in action: the blast radius of "Implementation needs
   rework" is recomputed from the graph, not hand-listed.
5. **Implementation re-executes (execution #2)**: this time the rework
   lands, and the fix is in place.
6. **The parallel branch re-runs**: UnitTesting, IntegrationTesting,
   SecurityReview, and Documentation all execute again from scratch (their
   prior "pass" from step 2 no longer counts - the code under test
   changed). This time IntegrationTesting passes on its first attempt.
7. **ReleaseReadiness** synchronizes on all four again, checklist is
   complete, change ticket `JIRA-4588` attached, human approves: "Regression
   reproduced then verified fixed under load; approved to ship."

## Validation & guardrails

No PII or secret exposure in this change - guardrails evaluate clean. The
interesting validation here is functional (the load test itself), not
policy - a reminder that guardrails and tests are separate, complementary
gates (§3.6 of `docs/architecture.md`).

## Expected outcome

| Metric | Expected value |
|---|---|
| Pipeline status | `Completed` |
| Retry count | ≥ 1 (IntegrationTesting's first failed attempt within its own budget) |
| Rollback count | 1 |
| Re-plan count | 1 |
| Human approvals | 3 - all approved |
| Guardrail findings | none blocking |

Asserted directly in `tests/UrlShortener.Orchestrator.Tests/ScenarioTests.cs::Brownfield_RecoversViaRollbackAndReplanAfterRegression`,
and at the engine level (with a minimal synthetic graph, decoupled from
this specific narrative) in
`OrchestrationEngineTests.cs::RunAsync_WhenDownstreamStageExhaustsRetries_RollsBackAndReplansUpstreamThenRecovers`.
