using SignalRMVC.FreezeMonitor.Models;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class RingBufferService
{
    private readonly object _sync = new();
    private readonly HealthSnapshot?[] _items;
    private int _nextIndex;
    private int _count;

    public RingBufferService(int capacity)
    {
        _items = new HealthSnapshot?[capacity];
    }

    public int Capacity => _items.Length;

    public void Add(HealthSnapshot snapshot)
    {
        lock (_sync)
        {
            _items[_nextIndex] = snapshot;
            _nextIndex = (_nextIndex + 1) % _items.Length;
            if (_count < _items.Length)
            {
                _count++;
            }
        }
    }

    public HealthSnapshot? GetLatest()
    {
        lock (_sync)
        {
            if (_count == 0)
            {
                return null;
            }

            var latestIndex = (_nextIndex - 1 + _items.Length) % _items.Length;
            return _items[latestIndex];
        }
    }

    public IReadOnlyList<HealthSnapshot> GetLatest(int maximumCount)
    {
        lock (_sync)
        {
            var take = Math.Min(Math.Max(maximumCount, 0), _count);
            var snapshots = new List<HealthSnapshot>(take);
            var start = (_nextIndex - take + _items.Length) % _items.Length;

            for (var index = 0; index < take; index++)
            {
                var snapshot = _items[(start + index) % _items.Length];
                if (snapshot is not null)
                {
                    snapshots.Add(snapshot);
                }
            }

            return snapshots;
        }
    }
}
