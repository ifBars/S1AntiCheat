using S1AntiCheat.API.Authorization;
using S1AntiCheat.API.Runtime;
using S1AntiCheat.API.Violations;

namespace S1AntiCheat.API;

/// <summary>
/// Provides dependency checks and access to the active S1 Anti-Cheat runtime.
/// </summary>
public static class AntiCheat
{
    private static readonly object SyncRoot = new();
    private static IAntiCheatRuntime? _runtime;

    /// <summary>Raised after the host records an anti-cheat violation.</summary>
    public static event Action<AntiCheatViolation>? ViolationReported;

    /// <summary>Gets whether the runtime mod has initialized in this process.</summary>
    public static bool IsRunning
    {
        get
        {
            lock (SyncRoot)
            {
                return _runtime != null;
            }
        }
    }

    /// <summary>Gets the active runtime version, or <see langword="null"/> when unavailable.</summary>
    public static Version? RuntimeVersion
    {
        get
        {
            lock (SyncRoot)
            {
                return _runtime?.Version;
            }
        }
    }

    /// <summary>Gets whether this process currently owns an active protected listen-host session.</summary>
    public static bool IsHostProtectionActive
    {
        get
        {
            lock (SyncRoot)
            {
                return _runtime?.IsHostProtectionActive == true;
            }
        }
    }

    /// <summary>
    /// Requires S1 Anti-Cheat and returns a consumer-scoped authorization handle.
    /// </summary>
    /// <param name="consumerId">A stable reverse-domain or similarly unique mod identifier.</param>
    /// <param name="minimumVersion">The minimum accepted runtime version.</param>
    /// <exception cref="AntiCheatUnavailableException">Thrown when the runtime is missing or too old.</exception>
    public static AntiCheatHandle Require(string consumerId, Version minimumVersion)
    {
        string normalizedConsumerId = NormalizeConsumerId(consumerId);
        if (minimumVersion == null)
        {
            throw new ArgumentNullException(nameof(minimumVersion));
        }

        IAntiCheatRuntime runtime = GetRequiredRuntime();
        if (runtime.Version.CompareTo(minimumVersion) < 0)
        {
            throw new AntiCheatUnavailableException(
                $"{normalizedConsumerId} requires S1 Anti-Cheat {minimumVersion} or newer; " +
                $"the active runtime is {runtime.Version}.");
        }

        return new AntiCheatHandle(normalizedConsumerId);
    }

    internal static IAntiCheatRuntime GetRequiredRuntime()
    {
        lock (SyncRoot)
        {
            return _runtime ?? throw new AntiCheatUnavailableException(
                "S1 Anti-Cheat is required but its runtime mod is not initialized. " +
                "Install the matching Mono or IL2CPP S1AntiCheat mod and restart the game.");
        }
    }

    internal static void RegisterRuntime(IAntiCheatRuntime runtime)
    {
        if (runtime == null)
        {
            throw new ArgumentNullException(nameof(runtime));
        }

        lock (SyncRoot)
        {
            if (_runtime != null && !ReferenceEquals(_runtime, runtime))
            {
                throw new InvalidOperationException("A different S1 Anti-Cheat runtime is already registered.");
            }

            _runtime = runtime;
        }
    }

    internal static void UnregisterRuntime(IAntiCheatRuntime runtime)
    {
        lock (SyncRoot)
        {
            if (ReferenceEquals(_runtime, runtime))
            {
                _runtime = null;
            }
        }
    }

    internal static void PublishViolation(AntiCheatViolation violation)
    {
        ViolationReported?.Invoke(violation);
    }

    private static string NormalizeConsumerId(string consumerId)
    {
        if (string.IsNullOrWhiteSpace(consumerId))
        {
            throw new ArgumentException("A stable consumer identifier is required.", nameof(consumerId));
        }

        string normalized = consumerId.Trim().ToLowerInvariant();
        if (normalized.Length > 96)
        {
            throw new ArgumentOutOfRangeException(nameof(consumerId), "Consumer identifiers may not exceed 96 characters.");
        }

        return normalized;
    }
}
