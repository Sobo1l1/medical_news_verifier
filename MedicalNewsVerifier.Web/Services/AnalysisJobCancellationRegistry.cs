using System.Collections.Concurrent;

namespace MedicalNewsVerifier.Web.Services;

public interface IAnalysisJobCancellationRegistry
{
    CancellationToken Register(Guid jobId);
    bool TryCancel(Guid jobId);
    void Remove(Guid jobId);
}

public sealed class AnalysisJobCancellationRegistry : IAnalysisJobCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();

    public CancellationToken Register(Guid jobId)
    {
        var cts = new CancellationTokenSource();
        if (!_sources.TryAdd(jobId, cts))
        {
            cts.Dispose();
            throw new InvalidOperationException($"Job {jobId} already registered.");
        }

        return cts.Token;
    }

    public bool TryCancel(Guid jobId)
    {
        if (!_sources.TryGetValue(jobId, out var cts))
        {
            return false;
        }

        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Remove(Guid jobId)
    {
        if (_sources.TryRemove(jobId, out var cts))
        {
            cts.Dispose();
        }
    }
}
