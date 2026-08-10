namespace S1AntiCheat.API;

/// <summary>
/// A consumer-scoped entry point for host-side action authorization.
/// </summary>
public sealed class AntiCheatHandle
{
    internal AntiCheatHandle(string consumerId)
    {
        ConsumerId = consumerId;
    }

    /// <summary>Gets the stable identifier of the consuming mod.</summary>
    public string ConsumerId { get; }

    /// <summary>
    /// Authorizes an incoming mod action against peer verification and a fixed-window rate limit.
    /// </summary>
    /// <param name="connectionId">The sender's FishNet connection identifier.</param>
    /// <param name="capability">A stable action name such as <c>trade.submit</c>.</param>
    /// <param name="actionLimit">The host-enforced action limit.</param>
    /// <returns>The authorization decision. The consuming mod must stop when denied.</returns>
    public AntiCheatDecision Authorize(
        int connectionId,
        string capability,
        AntiCheatActionLimit actionLimit)
    {
        return AntiCheat.GetRequiredRuntime().Authorize(
            ConsumerId,
            connectionId,
            NormalizeRequired(capability, nameof(capability)),
            actionLimit);
    }

    /// <summary>
    /// Reports semantic validation evidence owned by the consuming mod.
    /// </summary>
    public void ReportViolation(
        int connectionId,
        string capability,
        AntiCheatViolationSeverity severity,
        string reason)
    {
        AntiCheat.GetRequiredRuntime().ReportViolation(
            ConsumerId,
            connectionId,
            NormalizeRequired(capability, nameof(capability)),
            severity,
            NormalizeRequired(reason, nameof(reason)));
    }

    /// <summary>
    /// Attempts to resolve the anti-cheat state for a FishNet connection identifier.
    /// </summary>
    public bool TryGetPeer(int connectionId, out AntiCheatPeer peer)
    {
        return AntiCheat.GetRequiredRuntime().TryGetPeer(connectionId, out peer);
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }
}
