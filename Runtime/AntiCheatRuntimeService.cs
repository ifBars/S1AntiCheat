using S1AntiCheat.Bootstrap;
using MelonLoader;
using S1AntiCheat.API;
using S1AntiCheat.API.Authorization;
using S1AntiCheat.API.Peers;
using S1AntiCheat.API.Runtime;
using S1AntiCheat.API.Violations;
using S1AntiCheat.Configuration;
#if MONO
using InstanceFinder = FishNet.InstanceFinder;
#else
using InstanceFinder = Il2CppFishNet.InstanceFinder;
#endif

namespace S1AntiCheat.Runtime;

internal sealed class AntiCheatRuntimeService : IAntiCheatRuntime
{
    private readonly ConnectionRegistry _connections;
    private readonly ActionRateLimiter _rateLimiter = new();

    internal AntiCheatRuntimeService(ConnectionRegistry connections)
    {
        _connections = connections;
        _connections.ConnectionRemoved += OnConnectionRemoved;
    }

    public Version Version { get; } = new(ModInfo.Version);

    public bool IsHostProtectionActive => InstanceFinder.IsHost;

    public AntiCheatDecision Authorize(
        string consumerId,
        int connectionId,
        string capability,
        AntiCheatActionLimit actionLimit)
    {
        if (!InstanceFinder.IsHost)
        {
            return Denied(AntiCheatDecisionCode.HostProtectionInactive, 0UL,
                "Incoming actions must be authorized by the listen host.");
        }

        if (connectionId < 0 || string.IsNullOrWhiteSpace(capability))
        {
            return Denied(AntiCheatDecisionCode.InvalidRequest, 0UL,
                "The incoming action does not identify a valid connection and capability.");
        }

        if (connectionId == AdmissionPolicy.LocalHostConnectionId)
        {
            return ApplyRateLimit(consumerId, connectionId, capability, actionLimit, 0UL);
        }

        if (!_connections.TryGet(connectionId, out PeerState peer))
        {
            return Denied(AntiCheatDecisionCode.UnknownConnection, 0UL,
                "The sender is not known to S1 Anti-Cheat.");
        }

        if (peer.IsDenied)
        {
            return Denied(AntiCheatDecisionCode.PeerDenied, peer.SteamId,
                "The sender is denied for this session.");
        }

        if (!peer.IsAdmitted || !peer.IsVerified)
        {
            return Denied(AntiCheatDecisionCode.PeerVerificationPending, peer.SteamId,
                "The sender has not completed host verification.");
        }

        return ApplyRateLimit(consumerId, connectionId, capability, actionLimit, peer.SteamId);
    }

    public bool TryGetPeer(int connectionId, out AntiCheatPeer peer)
    {
        return _connections.TryGetPublic(connectionId, out peer);
    }

    public void ReportViolation(
        string consumerId,
        int connectionId,
        string capability,
        AntiCheatViolationSeverity severity,
        string reason)
    {
        _connections.TryGet(connectionId, out PeerState peer);
        ulong steamId = peer?.SteamId ?? 0UL;
        var violation = new AntiCheatViolation(
            consumerId,
            capability,
            connectionId,
            steamId,
            severity,
            reason);
        try
        {
            API.AntiCheat.PublishViolation(violation);
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"{ModInfo.LogPrefix} A violation event subscriber failed: {exception.Message}");
        }
        MelonLogger.Warning(
            $"{ModInfo.LogPrefix} VIOLATION consumer={consumerId} capability={capability} " +
            $"severity={severity} connection={connectionId} steamId={steamId} reason={reason}");

        if (severity == AntiCheatViolationSeverity.ExploitAttempt &&
            AntiCheatPreferences.DisconnectOnExploitAttempt.Value && peer != null)
        {
            _connections.Deny(peer);
            _connections.QueueDisconnect(peer);
        }
    }

    internal void Clear()
    {
        _connections.ConnectionRemoved -= OnConnectionRemoved;
        _rateLimiter.Clear();
    }

    private void OnConnectionRemoved(int connectionId)
    {
        _rateLimiter.RemoveConnection(connectionId);
    }

    private AntiCheatDecision ApplyRateLimit(
        string consumerId,
        int connectionId,
        string capability,
        AntiCheatActionLimit actionLimit,
        ulong steamId)
    {
        if (!_rateLimiter.TryAcquire(consumerId, connectionId, capability, actionLimit, DateTime.UtcNow))
        {
            return Denied(AntiCheatDecisionCode.RateLimited, steamId,
                $"The sender exceeded the {consumerId}:{capability} action limit.");
        }

        return new AntiCheatDecision(true, AntiCheatDecisionCode.Allowed, steamId, "Allowed by the listen host.");
    }

    private static AntiCheatDecision Denied(AntiCheatDecisionCode code, ulong steamId, string message)
    {
        return new AntiCheatDecision(false, code, steamId, message);
    }
}
