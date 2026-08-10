#if MONO
using NetworkConnection = FishNet.Connection.NetworkConnection;
#else
using NetworkConnection = Il2CppFishNet.Connection.NetworkConnection;
#endif

namespace S1AntiCheat.Runtime;

internal sealed class PeerState
{
    internal PeerState(int connectionId)
    {
        ConnectionId = connectionId;
    }

    internal int ConnectionId { get; }

    internal ulong SteamId { get; set; }

    internal bool IsAdmitted { get; set; }

    internal bool IsVerified { get; set; }

    internal bool IsDenied { get; set; }

    internal string ChallengeNonce { get; set; } = string.Empty;

    internal DateTime ChallengeDeadlineUtc { get; set; }

    internal DateTime NextChallengeSendUtc { get; set; }

    internal NetworkConnection? Connection { get; set; }
}
