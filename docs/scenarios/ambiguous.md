# Scenario 3: Ambiguous — "Make the Analytics Better"

**Code**: `src/UrlShortener.Orchestrator/Scenarios/AmbiguousScenario.cs`
**Run it**: `dotnet run --project src/UrlShortener.Orchestrator`
**Artifacts**: `artifacts/sample-runs/ambiguous.*`

This scenario demonstrates two governance paths in a single run: a human
**clarification checkpoint** resolving genuine ambiguity before any design
work starts, and a **policy guardrail safe-stop** catching a privacy-risk
decision the moment it's produced - a controlled halt that requires human
rework, not an automatic retry, because the problem is a policy violation,
not a transient fault.

## The ask

> "Make the analytics better."

No acceptance criteria, no defined scope, a subjective adjective doing all
the work. This is exactly the kind of input a real intake queue receives,
and the point of this scenario is to show the orchestrator *not* guessing
its way through it.

## 1. Requirement understanding & ambiguity detection

`RequirementsAgent`'s ambiguity detector flags the word "better" as
subjective with no measurable acceptance criteria. The normalized
requirement is explicitly marked "pending clarification" rather than
silently interpreted. Because `Requirements` is a human-approval-gated
stage, the engine stops here and surfaces the ambiguity in the approval
request context (visible to whoever reviews it, e.g. via
`ConsoleApprovalProvider` in an interactive run) - it does not proceed to
Architecture on a guess.

The scripted approval for this run represents a product owner resolving
the ambiguity, not rubber-stamping it:

> "Add per-country breakdown of clicks to the analytics endpoint using IP
> geolocation. Everything else ('better') is out of scope for this
> change."

This clarification is attached to the approval response and, on approval,
merged directly into the pipeline's shared context (`human_clarification`)
- so every downstream stage sees the *resolved* scope, not the original
vague ask. This is the human-approval-checkpoint mechanism from
`docs/architecture.md` §3.3 doing real work, not just gating a rubber
stamp.

## 2. Architecture — and where it goes wrong

`ArchitectureAgent` reads the clarification and proposes: add a
`CountryCode` aggregation to `GetUrlAnalyticsQuery`. To maximize
geolocation accuracy, it proposes **persisting the raw client IP address**
on each `ClickEvent`, in addition to the existing hash, so a batch job can
resolve country server-side. The trade-off is recorded honestly in the
decision lineage: *"Raw IP storage improves geolocation accuracy over
IP-hash-only, at the cost of a PII retention question - flagged for the
security/compliance gate."* Architecture's own approval gate passes this
through (a plausible design on its face, and the *policy* check is a
separate, independent gate - see `docs/architecture.md` §3.6) - it isn't
Architecture's job to be the enforcement point.

## 3. Implementation and the guardrail

`ImplementationAgent` "writes" the change and marks `exposes_raw_ip: true`
in the shared context - an honest signal of what the code actually does,
which is exactly what a policy guardrail needs to evaluate. Immediately
after the agent succeeds, `DataPrivacyGuardrail` evaluates the
Implementation stage's artifacts and finds:

> "Design proposes persisting/returning raw client IP addresses; policy
> requires hashing (PII minimization)." — **Blocking**

This is the same PII-minimization policy already implemented in the
product itself (`docs/architecture.md`'s decision table: "Hash the client
IP, never store it raw"). The guardrail catching it here means the
orchestrator enforces the product's own stated policy against a proposed
change to that product - not a hypothetical rule.

## 4. Safe-stop

A Blocking finding halts the entire pipeline immediately
(`OrchestrationEngine.TriggerSafeStop`), before UnitTesting, Documentation,
or the other parallel-branch stages ever run - there's no reason to spend
effort testing and documenting a design that policy has already rejected.
The `Implementation` stage is marked `FailedTerminal`; every other
not-yet-started stage stays `Pending`. This is deliberately **not** a
retry-and-recover path like the brownfield scenario: a rejected-by-policy
design isn't a transient fault an agent can retry its way past, so the
engine doesn't try - it stops and hands the decision back to a human
(e.g., "use IP-hash + a third-party geolocation service that only needs
the hash-adjacent coarse region, not the raw IP" would be the next
Architecture proposal in a follow-up run).

## Validation & guardrails

This scenario is the SecretScanningGuardrail/DataPrivacyGuardrail's reason
for existing. `SecretScanningGuardrail` finds nothing (no credential-like
literals here); `DataPrivacyGuardrail` is the one that fires, exactly as
designed.

## Expected outcome

| Metric | Expected value |
|---|---|
| Pipeline status | `SafeStopped` |
| Safe-stop reason | Contains `data-privacy` (the guardrail name) and the Implementation stage id |
| `human_clarification` artifact | Present, non-empty (the scoped requirement) |
| Requirements stage | `Completed` (approved, with clarification) |
| Architecture stage | `Completed` (auto-approved - no explicit script entry) |
| Implementation stage | `FailedTerminal` |
| UnitTesting / IntegrationTesting / SecurityReview / Documentation / ReleaseReadiness | `Pending` (never scheduled) |

Asserted directly in `tests/UrlShortener.Orchestrator.Tests/ScenarioTests.cs::Ambiguous_ClarifiesRequirementThenSafeStopsOnPolicyGuardrail`.
