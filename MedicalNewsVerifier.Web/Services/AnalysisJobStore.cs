using Microsoft.Extensions.Caching.Memory;

namespace MedicalNewsVerifier.Web.Services;

public interface IAnalysisJobStore
{
    void Create(Guid jobId);
    void Patch(Guid jobId, Action<AnalysisJobState> mutator);
    AnalysisJobState? TryGet(Guid jobId);
}

public sealed class AnalysisJobState
{
    public string Phase { get; set; } = "Pending";
    /// <summary>0 — источники, 1 — признаки, 2 — нейросеть, 3 — сохранение.</summary>
    public int StepIndex { get; set; }
    public bool FeaturesCompleted { get; set; }
    public bool NeuralCompleted { get; set; }
    public string? Message { get; set; }
    public int? HeuristicScore { get; set; }
    public int? LlmScore { get; set; }
    public int? CombinedScore { get; set; }
    public int? RecordId { get; set; }
    public string? Error { get; set; }
    public string? LlmSummaryPreview { get; set; }
}

public sealed class AnalysisJobStore(IMemoryCache cache, ILogger<AnalysisJobStore> logger) : IAnalysisJobStore
{
    private static string Key(Guid id) => $"analysis-job:{id:D}";

    public void Create(Guid jobId)
    {
        var state = new AnalysisJobState
        {
            Phase = "Started",
            Message = "Анализ поставлен в очередь."
        };
        cache.Set(Key(jobId), state, EntryOptions());
    }

    public void Patch(Guid jobId, Action<AnalysisJobState> mutator)
    {
        var key = Key(jobId);
        if (!cache.TryGetValue(key, out AnalysisJobState? state) || state is null)
        {
            logger.LogWarning("Analysis job {JobId} missing in cache; patch skipped", jobId);
            return;
        }

        mutator(state);
        cache.Set(key, state, EntryOptions());
    }

    public AnalysisJobState? TryGet(Guid jobId)
    {
        cache.TryGetValue(Key(jobId), out AnalysisJobState? state);
        return state;
    }

    private static MemoryCacheEntryOptions EntryOptions() =>
        new()
        {
            SlidingExpiration = TimeSpan.FromHours(2),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6)
        };
}
