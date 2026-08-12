namespace S1AntiCheat.API.Peers;

/// <summary>
/// Represents the anti-cheat state associated with a FishNet connection.
/// </summary>
public readonly struct AntiCheatPeer
{
    internal AntiCheatPeer(int connectionId, ulong steamId, bool admitted, bool verified, bool denied)
    {
        ConnectionId = connectionId;
        SteamId = steamId;
        IsAdmitted = admitted;
        IsVerified = verified;
        IsDenied = denied;
    }

    /// <summary>Gets the FishNet connection identifier.</summary>
    public int ConnectionId { get; }

    /// <summary>Gets the transport-verified SteamID.</summary>
    public ulong SteamId { get; }

    /// <summary>Gets whether the connection passed host admission policy.</summary>
    public bool IsAdmitted { get; }

    /// <summary>Gets whether the peer completed the client anti-cheat handshake.</summary>
    public bool IsVerified { get; }

    /// <summary>Gets whether the peer is denied for the current session.</summary>
    public bool IsDenied { get; }
}
