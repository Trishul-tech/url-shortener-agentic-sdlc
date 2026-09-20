# Specification

This document is the formal, consolidated spec for both halves of this
repository: the URL shortener product and the agentic SDLC orchestrator.
`docs/architecture.md` explains *why* things are built this way;
this document states *what* is built, precisely, as a reference.

## 1. Scope

| In scope | Out of scope |
|---|---|
| Create / redirect / deactivate / analytics for short URLs | Multi-tenant billing, teams, organizations |
| Rate limiting, SSRF protection, optional API-key auth | Full OAuth2/JWT per-user identity (documented next step) |
| Agentic orchestration engine: dependency graph, retry/fallback/rollback/safe-stop, governance, audit | A production task queue / distributed workers for the orchestrator |
| Three required scenarios (greenfield, brownfield, ambiguous) | Live orchestration against a real, continuously-changing codebase |
| One optional, tested, opt-in live-LLM `IAgent` (Anthropic) | A general-purpose "bring your own LLM provider" abstraction |

## 2. URL Shortener API

### 2.1 Endpoints

| Method | Path | Auth | Rate limit policy | Description |
|---|---|---|---|---|
| `POST` | `/api/v1/urls` | Optional `X-Api-Key` (see 2.4) | `create` (20/min/IP) | Create a short URL |
| `GET` | `/{code}` | None (public) | `redirect` (100/min/IP) | 302 redirect to target |
| `DELETE` | `/api/v1/urls/{code}` | Optional `X-Api-Key` | None | Deactivate a short URL |
| `GET` | `/api/v1/urls/{code}/analytics` | None | None | Click analytics for a date range |
| `GET` | `/health` | None | None | Liveness/readiness |

Full request/response contracts: `src/UrlShortener.Api/Contracts`, and live via Swagger (`/swagger`, Development only).

### 2.2 Validation rules (Create)

- `targetUrl`: required, absolute http/https URL, must pass the SSRF guard (2.3).
- `customAlias`: optional, 3-12 characters, alphanumeric, must not collide with an existing active code.
- `expiresAtUtc`: optional, must be in the future if present.

### 2.3 SSRF protection

Every `targetUrl` is checked by `SsrfGuard` before a short URL is created. Rejected if the URL is a literal IP, or resolves via DNS to any address in:

- Private ranges: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16
- Loopback: 127.0.0.0/8, IPv6 ::1
- Link-local: 169.254.0.0/16 (including the cloud-metadata address 169.254.169.254), IPv6 fe80::/10
- 0.0.0.0/8
- IPv6 unique-local: fc00::/7

DNS resolution is abstracted behind `IDnsResolver` so this stays testable without live network access. This is a point-in-time check made at creation time, not re-validated on redirect - see `docs/testing-limitations-tradeoffs.md` for the TOCTOU caveat.

### 2.4 Optional API-key authentication

`ApiKeyFilter` gates `POST /api/v1/urls` and `DELETE /api/v1/urls/{code}`. Unset (default): both endpoints are open. Set (via an `ApiKey` configuration value / environment variable): both endpoints require a matching `X-Api-Key` header, checked with a fixed-time comparison, or they return `401`. This is a single shared secret, not per-user identity - see `docs/testing-limitations-tradeoffs.md` item 4 for what it does and does not protect against.

## 3. Agentic Orchestrator

### 3.1 Stage graph

Eight stages, fixed shape, validated acyclic at construction:

    Requirements -> Architecture -> Implementation -> {UnitTesting, IntegrationTesting, SecurityReview, Documentation} -> ReleaseReadiness

Requirements and Architecture require human approval by default. The four post-Implementation stages run concurrently (bounded by `OrchestrationOptions.MaxDegreeOfParallelism`). ReleaseReadiness is the fan-in synchronization point and also requires approval.

### 3.2 Per-stage control flow

For each stage attempt: entry guardrail gate -> agent execution -> exit guardrail gate -> (on failure) retry up to the stage's budget -> (on exhaustion) fallback agent, tried once, if configured -> (on further failure, or no fallback) rollback to the nearest configured ancestor + re-plan its downstream subgraph, bounded by `MaxRollbacks` -> (if no rollback target, or budget exhausted) safe-stop the whole pipeline. See `docs/architecture.md` section 3.4 for the full reasoning and the diagram in the README.

### 3.3 Governance

- **Human approval** (`IApprovalProvider`): scripted for the three required scenarios (reproducible), a console implementation exists for interactive use, and a `Deferred` decision path is implemented for a real async transport (Slack/Teams/webhook) but unused by any scripted scenario.
- **Policy guardrails** (`IPolicyGuardrail`): `SecretScanningGuardrail` (blocking), `DataPrivacyGuardrail` (blocking - fires in the ambiguous scenario), `ChangeControlGuardrail` (warning-only). Evaluated at both entry and exit of every stage attempt.

### 3.4 Observability

- `AuditLog`: append-only, timestamped, correlation-ID-tagged event stream of everything that happened.
- `PipelineExecutionContext.Lineage`: ordered `DecisionRecord`s - what was decided, why, by whom - the "why" companion to the audit log's "what."
- `MetricsCollector`: success rate, retry/fallback/rollback/replan counts, mean time to recovery, total latency. Written to `artifacts/sample-runs/` per scenario run.

### 3.5 Agent model

`IAgent.ExecuteAsync(PipelineExecutionContext, attempt, CancellationToken) -> AgentOutcome`. Two implementations exist:

- **Deterministic simulated agents** (one per stage) - used by all three required scenarios, for reproducibility.
- **`AnthropicAgent`** - a real, tested implementation that calls the live Anthropic Messages API. Opt-in via `ANTHROPIC_API_KEY`; when set, `Program.cs` runs one additional live call through it (Requirements stage) after the three scripted scenarios complete, demonstrating the seam actually works end to end, not just in theory.

## 4. Non-functional requirements

| Requirement | How it's met |
|---|---|
| Deterministic, reproducible demo runs | Scripted approvals + simulated agents for the three required scenarios |
| No external services required to run | SQLite file, in-memory cache, no cloud dependency |
| Test suite runs with zero configuration | Every opt-in feature (SSRF's DNS resolver, the live-LLM agent, API-key auth) defaults to its safe/open state; nothing needs a secret to build or test |
| Full audit trail per run | `AuditLog` + `DecisionLineage`, written to `artifacts/sample-runs/` |
| CI on every push | `.github/workflows/ci.yml` - `dotnet build` + `dotnet test` on `ubuntu-latest` |

## 5. Assumptions and constraints

See `docs/engineering-summary.md` Assumptions section and `docs/testing-limitations-tradeoffs.md` for the complete, itemized list. The short version: this is a scoped take-home prototype, not a production deployment - single-instance, no per-user auth beyond the optional shared API key, and the orchestrator's governance scripts are demo scripts sized to what the three required scenarios need to demonstrate the mechanism.