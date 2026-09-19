using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Governance;

public enum GuardrailSeverity { Info, Warning, Blocking }

public sealed record GuardrailFinding(string GuardrailName, GuardrailSeverity Severity, string Message);

/// <summary>
/// A policy guardrail evaluates the artifacts produced by a stage against a
/// security/compliance/change-control rule. A Blocking finding triggers the
/// engine's safe-stop path; Warning findings are recorded in the audit
/// trail but do not halt the pipeline.
/// </summary>
public interface IPolicyGuardrail
{
    string Name { get; }
    GuardrailFinding? Evaluate(StageId stage, IReadOnlyDictionary<string, object> context);
}

/// <summary>Flags obvious secret-like literals in generated code artifacts (simulated static analysis).</summary>
public sealed class SecretScanningGuardrail : IPolicyGuardrail
{
    public string Name => "secret-scanning";

    public GuardrailFinding? Evaluate(StageId stage, IReadOnlyDictionary<string, object> context)
    {
        if (stage != StageId.Implementation && stage != StageId.SecurityReview) return null;
        if (!context.TryGetValue("code_artifacts", out var raw) || raw is not IEnumerable<string> files) return null;

        var suspicious = files.FirstOrDefault(f =>
            f.Contains("password=", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("apikey=", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("secret=", StringComparison.OrdinalIgnoreCase));

        return suspicious is null
            ? null
            : new GuardrailFinding(Name, GuardrailSeverity.Blocking, $"Hardcoded credential-like literal detected in artifact: '{suspicious}'.");
    }
}

/// <summary>Flags PII exposure risk - e.g. a proposal to log or return raw client IPs instead of a hash.</summary>
public sealed class DataPrivacyGuardrail : IPolicyGuardrail
{
    public string Name => "data-privacy";

    public GuardrailFinding? Evaluate(StageId stage, IReadOnlyDictionary<string, object> context)
    {
        if (stage != StageId.Implementation && stage != StageId.SecurityReview) return null;
        if (!context.TryGetValue("exposes_raw_ip", out var value) || value is not true) return null;

        return new GuardrailFinding(Name, GuardrailSeverity.Blocking,
            "Design proposes persisting/returning raw client IP addresses; policy requires hashing (PII minimization).");
    }
}

/// <summary>Requires a change ticket reference before release - a lightweight change-control gate.</summary>
public sealed class ChangeControlGuardrail : IPolicyGuardrail
{
    public string Name => "change-control";

    public GuardrailFinding? Evaluate(StageId stage, IReadOnlyDictionary<string, object> context)
    {
        if (stage != StageId.ReleaseReadiness) return null;
        if (context.TryGetValue("change_ticket", out var ticket) && ticket is string s && !string.IsNullOrWhiteSpace(s))
            return null;

        return new GuardrailFinding(Name, GuardrailSeverity.Warning, "No change ticket reference attached to this release.");
    }
}
