namespace S1AntiCheat.API.Peers;

internal enum AdmissionReason
{
    LocalHost,
    ExplicitAllowlist,
    SteamFriendInLobby,
    TrustedLobbyMemberCompatibility,
    FailOpen,
    InvalidTransportIdentity,
    SessionDenied,
    LobbyUnavailable,
    NotInCurrentLobby,
    UntrustedLobbyMember
}

internal readonly struct AdmissionDecision
{
    internal AdmissionDecision(bool allowed, AdmissionReason reason, ulong steamId)
    {
        Allowed = allowed;
        Reason = reason;
        SteamId = steamId;
    }

    internal bool Allowed { get; }

    internal AdmissionReason Reason { get; }

    internal ulong SteamId { get; }
}
