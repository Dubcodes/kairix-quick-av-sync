namespace Kairix.QuickAVSync.Services;

public sealed class AnalysisGeneration
{
    private long _current;
    public long Next() => Interlocked.Increment(ref _current);
    public bool IsCurrent(long generation) => Volatile.Read(ref _current) == generation;
}
