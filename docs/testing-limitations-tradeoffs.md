# Testing Approach, Limitations, and Trade-offs

## Testing approach

| Layer | What's tested | How |
|---|---|---|
| `UrlShortener.Domain.Tests` | Entity invariants: URL validation, expiry rules, active/deactivated/expired state transitions, click recording, value-object equality. | Pure unit tests, no mocking needed - the domain has no dependencies. |
| `UrlShortener.Application.Tests` | Handler logic: code generation + collision retry, custom-alias policy enforcement, cache-then-repository resolve path, analytics-write-failure isolation (a broken analytics write must never fail a redirect). | Unit tests against handlers with `NSubstitute` mocks for repositories/cache/clock/code-generator. |
| `UrlShortener.Api.IntegrationTests` | The real HTTP surface end-to-end: create → redirect → deactivate → analytics, validation errors, conflict on duplicate alias, 404/410 semantics. | `WebApplicationFactory<Program>` against the real `Program.cs` pipeline, pointed at a throwaway SQLite file per test run (`CustomWebApplicationFactory`). |
| `UrlShortener.Orchestrator.Tests` | The engine itself, independent of any specific scenario narrative: dependency-graph cycle detection and ready-set computation, retry-then-succeed, retry-exhaustion-with-no-rollback → safe-stop, retry-exhaustion-with-rollback → replan → recovery, rollback-budget-exhaustion → safe-stop, blocking-guardrail → immediate safe-stop, rejected-approval → safe-stop. Then, separately, the three required scenarios run end-to-end against the real engine and are asserted to reach their documented outcomes (see `docs/scenarios/`). | Unit tests against the engine with a minimal `TestAgent` (scriptable success/failure), plus full scenario tests with zero mocking. |

The engine tests and the scenario tests are intentionally split: the engine
tests prove the *mechanism* (retry/rollback/guardrail/approval logic) works
correctly on minimal synthetic graphs, decoupled from any specific SDLC
narrative; the scenario tests prove the three *required, narrative*
scenarios produce the outcomes documented in `docs/scenarios/`. A change
that breaks the mechanism fails fast in the first set; a change that breaks
one scenario's specific staging fails in the second, without needing to
re-derive engine behavior from a big end-to-end test.

### What isn't tested (and why that's an acceptable gap for this submission)

- **No load/performance tests.** The redirect hot path's cache-then-repository
  design is argued for in `docs/architecture.md`, and the brownfield
  scenario's *narrative* is a concurrency bug, but there's no actual
  concurrent-load test harness in this repo proving the current
  `ResolveShortUrlHandler` implementation is race-free under real
  concurrent load. It plausibly has the same class of race the brownfield
  scenario describes fixing (elsewhere) - see the honest caveat below.
- **No test against a real LLM-backed `IAgent`.** All agent tests use the
  deterministic simulated agents described in `docs/architecture.md` §3.8.
  The `IAgent` interface is exercised thoroughly; a real network-calling
  implementation of it is not, because none exists in this repo.
- **No mutation testing / property-based testing.** Standard example-based
  xUnit tests only.

## Known limitations

1. **This solution has not been compiled.** See the root `README.md` for
   why (sandboxed environment with no `dotnet` SDK / NuGet access). Every
   file was written and cross-checked by hand against known .NET 8 APIs,
   and the orchestration engine's control flow was hand-traced through all
   three scenarios and the engine-level unit tests to confirm the expected
   sequence of events - but `dotnet build && dotnet test` has not actually
   been run. **Run it first**, and treat any compiler error you find as a
   real bug to report, not a sign the approach is wrong.
2. **`ResolveShortUrlHandler`'s cache-then-repository path is plausibly
   racy under real concurrent load**, in exactly the way the brownfield
   scenario's narrative describes fixing (two concurrent misses can both
   fall through to the repository and both record a click; see
   `docs/architecture.md`'s Resolve flow). The scenario demonstrates how
   the *orchestrator* would find, fix, and verify such a bug - it does not
   claim the fix has actually been applied to this repo's handler. Fixing
   it for real (request-coalescing, as the scenario's own architecture
   stage proposes) is the natural next PR.
3. **Single-instance scale only** - in-memory cache, SQLite file, IP-keyed
   in-process rate limiting. Documented explicitly in
   `docs/architecture.md` § Scale limitations, not hidden.
4. **No real authentication.** `ownerId` is a caller-supplied string
   compared for equality - it prevents accidental cross-owner edits, not
   malicious ones. A real deployment needs real auth before this field
   means anything security-wise.
5. **The orchestrator's approval and guardrail scripts are demo scripts.**
   `ScriptedApprovalProvider` and the three `IPolicyGuardrail`
   implementations are deliberately simple and cover exactly what the
   three required scenarios need to demonstrate the mechanism. A
   production rollout would need a real approval transport (Slack/Teams/
   ticketing webhook - the `IApprovalProvider.Deferred` path is designed
   for this but unused by any scripted scenario) and a broader guardrail
   set (dependency vulnerability scanning, license compliance, etc.).
6. **Round-based scheduling, not fully work-conserving** (see
   `docs/architecture.md` §3.2): a stage that becomes ready partway through
   a round waits for the round boundary rather than starting immediately.
   Chosen deliberately for deterministic, testable ordering; would need to
   become event-driven if stage latencies ever shrank to where that gap
   matters.
7. **`_states` (per-stage runtime state) uses a `ConcurrentDictionary`
   for thread-safety across parallel stage tasks, but the engine's overall
   control flow (compute ready set → launch round → await round → repeat)
   is not lock-free/wait-free reasoned about beyond "each round's tasks
   only ever mutate their own stage's entry, except rollback, which
   explicitly reassigns several entries at once and is only ever called
   from within a single stage's own execution path." This has been traced
   by hand, not stress-tested with concurrent rollback races.

## Trade-offs (explicit, not implicit)

| Trade-off | Choice made | What was given up |
|---|---|---|
| Agent realism vs. reproducibility | Deterministic simulated agents | Not demonstrating a live LLM call inside the orchestrator itself (the seam for one is real and documented, though - `IAgent`) |
| Scheduling simplicity vs. work-conservation | Round-based scheduling | Some latency in edge cases where a stage becomes ready mid-round |
| Product scope vs. orchestrator depth | Kept the URL shortener deliberately small (4 endpoints, no auth) | More time went into the orchestration engine, which is what the assignment weights as the "critical differentiator" |
| EF Core migrations vs. `EnsureCreated()` | `EnsureCreated()` for this submission | A real migration history - documented as the immediate next step in `docs/architecture.md` § Data & Migrations, with the exact commands to run |
| Rollback scope | Roll back to the nearest configured ancestor and re-plan its *entire* transitive downstream subgraph, even stages that already passed | Simplicity and correctness (never leaves a stale "passed" result standing against changed code) over minimizing re-run cost; a more surgical re-plan (only re-run stages whose actual inputs changed) is possible future work |
