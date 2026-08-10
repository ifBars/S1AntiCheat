using MelonLoader;
using S1AntiCheat.API;
using S1AntiCheat.API.Services;
#if MONO
using NetworkConnection = FishNet.Connection.NetworkConnection;
#else
using NetworkConnection = Il2CppFishNet.Connection.NetworkConnection;
#endif

namespace S1AntiCheat.Runtime;

internal sealed class ConnectionRegistry
{
    private readonly Dictionary<int, PeerState> _peers = new();
    private readonly HashSet<ulong> _sessionDenylist = new();
    private readonly List<NetworkConnection> _pendingDisconnects = new();
    private HashSet<ulong> _explicitAllowlist = new();

    internal event Action<int>? ConnectionRemoved;

    internal ISet<ulong> DeniedSteamIds => _sessionDenylist;

    internal ISet<ulong> ExplicitlyAllowedSteamIds => _explicitAllowlist;

    internal IEnumerable<PeerState> Peers => _peers.Values;

    internal void Reset(string? allowedSteamIds)
    {
        Clear();
        _explicitAllowlist = AdmissionPolicy.ParseSteamIdSet(allowedSteamIds);
        if (_explicitAllowlist.Count > 0)
        {
            MelonLogger.Msg($"{Constants.LogPrefix} Loaded {_explicitAllowlist.Count} explicitly allowed SteamID(s).");
        }
    }

    internal PeerState Admit(int connectionId, ulong steamId, NetworkConnection? connection = null)
    {
        PeerState peer = Begin(connectionId, steamId, connection);
        peer.IsAdmitted = true;
        return peer;
    }

    internal PeerState Begin(int connectionId, ulong steamId, NetworkConnection? connection = null)
    {
        var peer = new PeerState(connectionId)
        {
            SteamId = steamId,
            Connection = connection
        };
        _peers[connectionId] = peer;
        return peer;
    }

    internal PeerState Attach(NetworkConnection connection)
    {
        PeerState peer = GetOrCreate(connection.ClientId);
        peer.Connection = connection;
        if (peer.SteamId == 0UL)
        {
            AdmissionPolicy.TryParseSteamId(connection.GetAddress(), out ulong steamId);
            peer.SteamId = steamId;
        }

        return peer;
    }

    internal void MarkVerified(int connectionId)
    {
        PeerState peer = GetOrCreate(connectionId);
        peer.IsVerified = true;
    }

    internal void Deny(PeerState peer)
    {
        peer.IsDenied = true;
        peer.IsVerified = false;
        if (peer.SteamId != 0UL)
        {
            _sessionDenylist.Add(peer.SteamId);
        }
    }

    internal bool TryGet(int connectionId, out PeerState peer)
    {
        return _peers.TryGetValue(connectionId, out peer!);
    }

    internal void Remove(int connectionId)
    {
        _peers.Remove(connectionId);
        _pendingDisconnects.RemoveAll(connection => connection.ClientId == connectionId);
        ConnectionRemoved?.Invoke(connectionId);
    }

    internal bool TryGetPublic(int connectionId, out AntiCheatPeer peer)
    {
        if (_peers.TryGetValue(connectionId, out PeerState? state))
        {
            peer = new AntiCheatPeer(
                state.ConnectionId,
                state.SteamId,
                state.IsAdmitted,
                state.IsVerified,
                state.IsDenied);
            return true;
        }

        peer = default;
        return false;
    }

    internal void QueueDisconnect(PeerState peer)
    {
        if (peer.Connection == null || peer.Connection.ClientId < 0 ||
            _pendingDisconnects.Any(connection => connection.ClientId == peer.Connection.ClientId))
        {
            return;
        }

        _pendingDisconnects.Add(peer.Connection);
    }

    internal void FlushPendingDisconnects()
    {
        for (int index = _pendingDisconnects.Count - 1; index >= 0; index--)
        {
            NetworkConnection connection = _pendingDisconnects[index];
            _pendingDisconnects.RemoveAt(index);
            try
            {
                if (connection.ClientId >= 0 && !connection.Disconnecting)
                {
                    connection.Disconnect(true);
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Warning($"{Constants.LogPrefix} Failed to disconnect connection {connection.ClientId}: {exception.Message}");
            }
        }
    }

    internal void Clear()
    {
        _peers.Clear();
        _sessionDenylist.Clear();
        _pendingDisconnects.Clear();
        _explicitAllowlist.Clear();
    }

    private PeerState GetOrCreate(int connectionId)
    {
        if (!_peers.TryGetValue(connectionId, out PeerState? peer))
        {
            peer = new PeerState(connectionId);
            _peers.Add(connectionId, peer);
        }

        return peer;
    }
}
