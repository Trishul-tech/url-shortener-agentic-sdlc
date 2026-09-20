using System.Diagnostics;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Observability;

/// <summary>
/// Tracks the reliability metrics the assignment calls out explicitly:
/// success rate, retry/rollback frequency, MTTR, and end-to-end latency.
/// One instance per pipeline run; thread-safe because parallel stages
/// report concurrently.
/// </summary>
public sealed class MetricsCollector
{
    private readonly object _lock = new();
    private readonly Stopwatch _pipelineClock = Stopwatch.StartNew();
    private readonly Dictionary<StageId, Stopwatch> _stageClocks = new();
    private readonly Dictionary<StageId, DateTimeOffset> _firstFailureAt = new();
    private readonly Dictionary<StageId, DateTimeOffset> _recoveredAt = new();

    private int _stagesAttempted;
    private int _stagesSucceeded;
    private int _stagesFailedTerminally;
    private int _retryCount;
    private int _rollbackCount;
    private int _replanCount;
    private int _fallbackCount;

    public void StageStarted(StageId stage)
    {
        lock (_lock)
        {
            _stagesAttempted++;
            if (!_stageClocks.ContainsKey(stage))
                _stageClocks[stage] = Stopwatch.StartNew();
        }
    }

    public void StageSucceeded(StageId stage)
    {
        lock (_lock)
        {
            _stagesSucceeded++;
            if (_firstFailureAt.ContainsKey(stage) && !_recoveredAt.ContainsKey(stage))
                _recoveredAt[stage] = DateTimeOffset.UtcNow;
        }
    }

    public void StageFailed(StageId stage, bool terminal)
    {
        lock (_lock)
        {
            if (!_firstFailureAt.ContainsKey(stage))
                _firstFailureAt[stage] = DateTimeOffset.UtcNow;
            if (terminal) _stagesFailedTerminally++;
        }
    }

    public void RetryRecorded() { lock (_lock) _retryCount++; }
    public void RollbackRecorded() { lock (_lock) _rollbackCount++; }
    public void ReplanRecorded() { lock (_lock) _replanCount++; }
    public void FallbackRecorded() { lock (_lock) _fallbackCount++; }

    public RunMetrics Snapshot()
    {
        lock (_lock)
        {
            var mttrSamples = _recoveredAt
                .Where(kv => _firstFailureAt.ContainsKey(kv.Key))
                .Select(kv => (kv.Value - _firstFailureAt[kv.Key]).TotalMilliseconds)
                .ToList();

            var successRate = _stagesAttempted == 0 ? 1.0 : (double)_stagesSucceeded / _stagesAttempted;

            return new RunMetrics(
                TotalLatencyMs: _pipelineClock.Elapsed.TotalMilliseconds,
                StagesAttempted: _stagesAttempted,
                StagesSucceeded: _stagesSucceeded,
                StagesFailedTerminally: _stagesFailedTerminally,
                SuccessRate: Math.Round(successRate, 4),
                RetryCount: _retryCount,
                RollbackCount: _rollbackCount,
                ReplanCount: _replanCount,
                FallbackCount: _fallbackCount,
                MeanTimeToRecoveryMs: mttrSamples.Count == 0 ? null : Math.Round(mttrSamples.Average(), 2));
        }
    }
}

public sealed record RunMetrics(
    double TotalLatencyMs,
    int StagesAttempted,
    int StagesSucceeded,
    int StagesFailedTerminally,
    double SuccessRate,
    int RetryCount,
    int RollbackCount,
    int ReplanCount,
    int FallbackCount,
    double? MeanTimeToRecoveryMs);