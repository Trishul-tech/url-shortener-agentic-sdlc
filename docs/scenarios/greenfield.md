# Scenario 1: Greenfield — QR Code Generation

**Code**: `src/UrlShortener.Orchestrator/Scenarios/GreenfieldScenario.cs`
**Run it**: `dotnet run --project src/UrlShortener.Orchestrator` (runs all three; this one first)
**Artifacts**: `artifacts/sample-runs/greenfield.*`

## The ask

> Add QR code generation: `GET /api/v1/urls/{code}/qrcode` returns a PNG QR
> code encoding the short link, so print/offline channels can use
> shortened URLs.

A new feature, built from scratch, against a clear and well-defined
requirement. This is the baseline: the happy path through the full graph,
plus one self-healing retry, with no rollback needed.

## 1. Requirement understanding & decomposition

`RequirementsAgent` runs its ambiguity detector against the raw text and
finds no vague/subjective terms ("better", "improve", etc.) - the
requirement is normalized as-is. Because `Requirements` is a
human-approval-gated stage, the engine still stops for sign-off before
Architecture starts; the scripted approver (`eng-lead`) approves with the
rationale "Clear, well-scoped requirement." No clarification round-trip is
needed here (contrast with `ambiguous.md`).

## 2. Architecture & design

`ArchitectureAgent` proposes: a new `GET /qrcode` endpoint, a new
`QrCodeService` (in Infrastructure), reusing the existing `ICacheService`
to cache rendered images by code, and explicitly **no schema changes**.
Two trade-offs are recorded in the decision lineage:

- On-demand PNG generation over precomputed storage (avoids unbounded blob
  growth).
- The existing in-memory cache is "good enough" for prototype QPS - flagged
  as a documented limitation, not silently accepted.

Impacted modules: `UrlShortener.Api/Endpoints`, a new
`UrlShortener.Infrastructure/Services/QrCodeService.cs`. The architecture
stage is also human-approval-gated; scripted approval: "Design is minimal
and reuses existing caching."

## 3. Implementation

`ImplementationAgent` "writes" `QrCodeService.cs` and `QrCodeEndpoints.cs`
and succeeds on the first attempt - no failure injected here, because the
interesting failure mode for this scenario lives in testing, not the build.

## 4. Testing, security, documentation (parallel branch)

Once Implementation completes, four stages become ready **in the same
round**, with no ordering constraint between them - the engine runs them
concurrently:

- **UnitTesting** is scripted to fail its *first* attempt
  ("1 unit test failed: image-diff assertion flaked on the CI runner
  \[known-flaky infra, not a code defect\]") and pass its second - within
  its `MaxRetries: 1` budget (2 total attempts). This demonstrates
  **bounded, self-healing retry**: the engine retries automatically, records
  the retry in the audit log and metrics (`RetryCount` ≥ 1), and never
  needs to involve a human or roll anything back, because the failure
  resolved itself within budget.
- **IntegrationTesting** passes on the first attempt (3 scenarios: active
  code returns 200, unknown code returns 404, expired code returns 410).
- **SecurityReview** reviews the two touched files, notes no PII handling
  concerns (the feature doesn't touch `exposes_raw_ip`), and passes.
- **Documentation** drafts the README/OpenAPI delta.

## 5. Release readiness (synchronization point)

`ReleaseReadiness` only becomes ready once *all four* of the above are
`Completed` - the fan-in / synchronization the orchestration model is built
around. Its agent checks the release checklist (unit tests, integration
tests, security review, docs all present), attaches change ticket
`JIRA-4521`, and requests final human sign-off. Scripted approval:
"All gates green; ship it."

## Validation & guardrails

All three policy guardrails (`SecretScanningGuardrail`,
`DataPrivacyGuardrail`, `ChangeControlGuardrail`) evaluate on every relevant
stage and find nothing to block - this feature never touches PII or adds
secrets, and the release has a ticket attached.

## Expected outcome

| Metric | Expected value |
|---|---|
| Pipeline status | `Completed` |
| Retry count | 1 (UnitTesting's flaky first attempt) |
| Rollback count | 0 |
| Re-plan count | 0 |
| Human approvals | 3 (Requirements, Architecture, ReleaseReadiness) - all approved |
| Guardrail findings | none blocking |

This is asserted directly in `tests/UrlShortener.Orchestrator.Tests/ScenarioTests.cs::Greenfield_CompletesWithOneSelfHealingRetryAndNoRollback`.
