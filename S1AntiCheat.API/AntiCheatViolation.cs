namespace S1AntiCheat.API;

/// <summary>
/// Classifies the severity of an integration-reported anti-cheat violation.
/// </summary>
public enum AntiCheatViolationSeverity
{
    /// <summary>Informational evidence that should not affect the session.</summary>
    Information,

    /// <summary>Suspicious behavior that should be logged for review.</summary>
    Suspicious,

    /// <summary>An impossible or explicitly unauthorized action.</summary>
    ExploitAttempt
}
/// <summary>
/// Contains a violation reported by S1 Anti-Cheat or a consuming mod.
/// </summary>
public sealed class AntiCheatViolation
{
    internal AntiCheatViolation(
        string consumerId,
        string capability,
        int connectionId,
        ulong steamId,
        AntiCheatViolationSeverity severity,
        string reason)
    {
        ConsumerId = consumerId;
        Capability = capability;
        ConnectionId = connectionId;
        SteamId = steamId;
        Severity = severity;
        Reason = reason;
        TimestampUtc = DateTime.UtcNow;
    }

    /// <summary>Gets the stable consuming mod identifier.</summary>
    public string ConsumerId { get; }

    /// <summary>Gets the protected action or capability.</summary>
    public string Capability { get; }

    /// <summary>Gets the FishNet connection identifier.</summary>
    public int ConnectionId { get; }

    /// <summary>Gets the transport-verified SteamID when available.</summary>
    public ulong SteamId { get; }

    /// <summary>Gets the violation severity.</summary>
    public AntiCheatViolationSeverity Severity { get; }

    /// <summary>Gets the local diagnostic reason.</summary>
    public string Reason { get; }

    /// <summary>Gets when the violation was recorded.</summary>
    public DateTime TimestampUtc { get; }
}
