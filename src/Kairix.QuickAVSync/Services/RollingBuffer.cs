namespace Kairix.QuickAVSync.Services;

public sealed class RollingBuffer<T>(int capacity, Func<T, long>? timestampSelector = null)
{
    private readonly object _gate = new();
    private T[] _items = new T[Math.Max(1, capacity)];
    private int _start, _count;
    public int Capacity { get { lock (_gate) return _items.Length; } }
    public int Count { get { lock (_gate) return _count; } }

    public void Add(T item)
    {
        lock (_gate)
        {
            var index = (_start + _count) % _items.Length;
            if (_count == _items.Length) { _items[_start] = item; _start = (_start + 1) % _items.Length; }
            else { _items[index] = item; _count++; }
        }
    }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_gate) return Enumerable.Range(0, _count).Select(i => _items[(_start + i) % _items.Length]).ToArray();
    }

    public IReadOnlyList<T> Range(long fromInclusive, long toInclusive)
    {
        if (timestampSelector is null) throw new InvalidOperationException("No timestamp selector was supplied.");
        return Snapshot().Where(x => { var t = timestampSelector(x); return t >= fromInclusive && t <= toInclusive; }).ToArray();
    }

    public void Resize(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        lock (_gate)
        {
            var keep = Snapshot().TakeLast(capacity).ToArray();
            _items = new T[capacity]; Array.Copy(keep, _items, keep.Length); _start = 0; _count = keep.Length;
        }
    }
    public void Clear() { lock (_gate) { _start = 0; _count = 0; Array.Clear(_items); } }
}
