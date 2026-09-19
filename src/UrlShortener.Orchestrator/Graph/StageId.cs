namespace UrlShortener.Orchestrator.Graph;

/// <summary>
/// The SDLC lifecycle stages the orchestrator coordinates. Testing,
/// Documentation and SecurityReview all depend only on Implementation and
/// have no dependency on each other, so the engine schedules them
/// concurrently and synchronizes at ReleaseReadiness - this is the
/// "non-linear, stateful execution" the assignment calls for, not a
/// linear requirements -> ... -> release chain.
/// </summary>
public enum StageId
{
    Requirements,
    Architecture,
    Implementation,
    UnitTesting,
    IntegrationTesting,
    SecurityReview,
    Documentation,
    ReleaseReadiness
}
