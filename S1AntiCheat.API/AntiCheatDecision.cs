namespace S1AntiCheat.API;

/// <summary>
/// Identifies why an integration action was accepted or rejected.
/// </summary>
public enum AntiCheatDecisionCode
{
    /// <summary>The host accepted the action.</summary>
    Allowed,

    /// <summary>The action was evaluated outside the listen host.</summary>
    HostProtectionInactive,

    /// <summary>The FishNet connection is not known to the anti-cheat runtime.</summary>
    UnknownConnection,

    /// <summary>The peer has not completed client anti-cheat verification.</summary>
    PeerVerificationPending,

    /// <summary>The peer was denied for the current session.</summary>
    PeerDenied,

    /// <summary>The integration exceeded its configured action limit.</summary>
    RateLimited,

    /// <summary>The integration supplied an invalid request.</summary>
    InvalidRequest
}

/// <summary>
/// Describes the listen host's authorization decision for a mod action.
/// </summary>
public readonly struct AntiCheatDecision
{
    internal AntiCheatDecision(bool allowed, AntiCheatDecisionCode code, ulong steamId, string message)
    {
        Allowed = allowed;
        Code = code;
        SteamId = steamId;
        Message = message ?? string.Empty;
    }

    /// <summary>Gets whether the action may proceed.</summary>
    public bool Allowed { get; }

    /// <summary>Gets the stable decision code.</summary>
    public AntiCheatDecisionCode Code { get; }

    /// <summary>Gets the transport-verified SteamID when available.</summary>
    public ulong SteamId { get; }

    /// <summary>Gets a diagnostic message intended for local logs.</summary>
    public string Message { get; }
}

