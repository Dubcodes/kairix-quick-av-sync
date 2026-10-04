namespace Kairix.QuickAVSync.Services;

public readonly record struct LatestRequestToken<T>(long Generation, T Value);

/// <summary>Tracks the newest request so late asynchronous completions cannot commit stale state.</summary>
public sealed class LatestRequestTracker<T>
{
    private readonly object _gate = new();
    private long _generation;
    private LatestRequestToken<T> _current;

    public LatestRequestToken<T> Begin(T value)
    {
        lock (_gate)
        {
            _current = new(++_generation, value);
            return _current;
        }
    }

    public bool IsCurrent(LatestRequestToken<T> token)
    {
        lock (_gate)
            return token.Generation == _current.Generation && EqualityComparer<T>.Default.Equals(token.Value, _current.Value);
    }
}

/// <summary>Admits at most one operation. A canceled operation retains ownership until it actually exits.</summary>
public sealed class SingleFlightOperationGate
{
    private int _active;
    public bool IsActive => Volatile.Read(ref _active) != 0;

    public bool TryEnter(out IDisposable? lease)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) { lease = null; return false; }
        lease = new Lease(this);
        return true;
    }

    private sealed class Lease(SingleFlightOperationGate owner) : IDisposable
    {
        private SingleFlightOperationGate? _owner = owner;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is not null) Volatile.Write(ref current._active, 0);
        }
    }
}
