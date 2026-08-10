namespace S1AntiCheat.API.Services;

internal sealed class ActionRateLimiter
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, WindowState> _windows = new(StringComparer.Ordinal);

    internal bool TryAcquire(
        string consumerId,
        int connectionId,
        string capability,
        AntiCheatActionLimit limit,
        DateTime utcNow)
    {
        string key = $"{consumerId}\n{connectionId}\n{capability}";
        lock (_syncRoot)
        {
            if (!_windows.TryGetValue(key, out WindowState? state) || utcNow - state.StartedUtc >= limit.Window)
            {
                _windows[key] = new WindowState(utcNow, 1);
                return true;
            }

            if (state.Count >= limit.MaximumActions)
            {
                return false;
            }

            state.Count++;
            return true;
        }
    }

    internal void RemoveConnection(int connectionId)
    {
        string marker = $"\n{connectionId}\n";
        lock (_syncRoot)
        {
            string[] keys = _windows.Keys.Where(key => key.Contains(marker, StringComparison.Ordinal)).ToArray();
            foreach (string key in keys)
            {
                _windows.Remove(key);
            }
        }
    }

    internal void Clear()
    {
        lock (_syncRoot)
        {
            _windows.Clear();
        }
    }

    private sealed class WindowState
    {
        internal WindowState(DateTime startedUtc, int count)
        {
            StartedUtc = startedUtc;
            Count = count;
        }

        internal DateTime StartedUtc { get; }

        internal int Count { get; set; }
    }
}
