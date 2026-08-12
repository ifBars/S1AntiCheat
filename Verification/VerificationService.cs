using S1AntiCheat.Bootstrap;
using MelonLoader;
using S1AntiCheat.API.Verification;
using S1AntiCheat.Configuration;
using S1AntiCheat.Networking;
using S1AntiCheat.Runtime;
#if MONO
using InstanceFinder = FishNet.InstanceFinder;
using NetworkConnection = FishNet.Connection.NetworkConnection;
#else
using InstanceFinder = Il2CppFishNet.InstanceFinder;
using NetworkConnection = Il2CppFishNet.Connection.NetworkConnection;
#endif

namespace S1AntiCheat.Verification;

internal sealed class VerificationService
{
    private readonly ConnectionRegistry _connections;
    private readonly IntegrityMessaging _messaging;
    private readonly ModManifestService _manifestService;
    private DateTime _nextHostTickUtc;

    internal VerificationService(
        ConnectionRegistry connections,
        IntegrityMessaging messaging,
        ModManifestService manifestService)
    {
        _connections = connections;
        _messaging = messaging;
        _manifestService = manifestService;
        _messaging.ClientMessageReceived += OnClientMessage;
        _messaging.ServerMessageReceived += OnServerMessage;
    }

    internal void Tick()
    {
        if (!InstanceFinder.IsHost || !_messaging.IsReady || DateTime.UtcNow < _nextHostTickUtc)
        {
            return;
        }

        _nextHostTickUtc = DateTime.UtcNow.AddMilliseconds(500);
        foreach (var pair in InstanceFinder.ServerManager.Clients)
        {
            NetworkConnection? connection = pair.Value;
            if (connection == null || connection.IsLocalClient)
            {
                continue;
            }

            PeerState peer = _connections.Attach(connection);
            if (!peer.IsAdmitted || peer.IsDenied || peer.IsVerified)
            {
                continue;
            }

            if (!AntiCheatPreferences.RequireClientAntiCheat.Value)
            {
                peer.IsVerified = true;
                continue;
            }

            if (peer.ChallengeNonce.Length == 0)
            {
                BeginChallenge(peer);
                continue;
            }

            if (DateTime.UtcNow >= peer.ChallengeDeadlineUtc)
            {
                Reject(peer, "Client anti-cheat verification timed out.");
                continue;
            }

            if (DateTime.UtcNow >= peer.NextChallengeSendUtc)
            {
                SendChallenge(peer);
            }
        }
    }

    internal void Dispose()
    {
        _messaging.ClientMessageReceived -= OnClientMessage;
        _messaging.ServerMessageReceived -= OnServerMessage;
    }

    private void BeginChallenge(PeerState peer)
    {
        int timeout = Math.Max(3, AntiCheatPreferences.VerificationTimeoutSeconds.Value);
        peer.ChallengeNonce = Guid.NewGuid().ToString("N");
        peer.ChallengeDeadlineUtc = DateTime.UtcNow.AddSeconds(timeout);
        SendChallenge(peer);
    }

    private void SendChallenge(PeerState peer)
    {
        IReadOnlyList<ModDescriptor> hostMods = _manifestService.Build();
        ISet<string> ignoredNames = ModVerificationPolicy.ParseNames(AntiCheatPreferences.IgnoredModNames.Value);
        string fingerprint = ModVerificationPolicy.ComputeFingerprint(hostMods, ignoredNames);
        int remainingSeconds = Math.Max(1, (int)Math.Ceiling((peer.ChallengeDeadlineUtc - DateTime.UtcNow).TotalSeconds));
        string payload = WireCodec.EncodeChallenge(peer.ChallengeNonce, remainingSeconds, fingerprint);
        if (peer.Connection != null && _messaging.SendToClient(peer.Connection, payload))
        {
            peer.NextChallengeSendUtc = DateTime.UtcNow.AddSeconds(1);
        }
    }

    private void OnClientMessage(string payload)
    {
        if (InstanceFinder.IsHost || !WireCodec.TryDecodeChallenge(payload, out string nonce))
        {
            if (WireCodec.TryDecodeResult(payload, out bool allowed, out string message))
            {
                MelonLogger.Msg($"{ModInfo.LogPrefix} Host verification {(allowed ? "passed" : "failed")}: {message}");
            }

            return;
        }

        IReadOnlyList<ModDescriptor> mods = _manifestService.Build();
        if (!_messaging.SendToServer(WireCodec.EncodeReport(nonce, mods)))
        {
            MelonLogger.Warning($"{ModInfo.LogPrefix} Could not submit the client verification report.");
        }
    }

    private void OnServerMessage(NetworkConnection connection, string payload)
    {
        if (!InstanceFinder.IsHost)
        {
            return;
        }

        PeerState peer = _connections.Attach(connection);
        if (peer.IsDenied)
        {
            return;
        }

        if (!peer.IsAdmitted)
        {
            Reject(peer, "The connection did not pass host admission policy.");
            return;
        }

        if (!WireCodec.TryDecodeReport(payload, out string runtimeVersion, out string nonce, out IReadOnlyList<ModDescriptor> mods))
        {
            Reject(peer, "The client sent an invalid anti-cheat report.");
            return;
        }

        if (peer.ChallengeNonce.Length == 0 || !string.Equals(peer.ChallengeNonce, nonce, StringComparison.Ordinal))
        {
            Reject(peer, "The client anti-cheat challenge did not match.");
            return;
        }

        if (peer.IsVerified)
        {
            _messaging.SendToClient(connection, WireCodec.EncodeResult(true, "Client anti-cheat verification passed."));
            return;
        }

        if (!Version.TryParse(runtimeVersion, out Version? clientVersion) ||
            clientVersion.CompareTo(new Version(ModInfo.Version)) < 0)
        {
            Reject(peer, $"S1 Anti-Cheat {ModInfo.Version} or newer is required.");
            return;
        }

        ISet<string> ignoredNames = ModVerificationPolicy.ParseNames(AntiCheatPreferences.IgnoredModNames.Value);
        string hostFingerprint = ModVerificationPolicy.ComputeFingerprint(_manifestService.Build(), ignoredNames);
        ModVerificationResult result = ModVerificationPolicy.Evaluate(
            mods,
            AntiCheatPreferences.ParsedVerificationMode,
            hostFingerprint,
            ignoredNames,
            ModVerificationPolicy.ParseNames(AntiCheatPreferences.DeniedModNames.Value),
            ModVerificationPolicy.ParseHashes(AntiCheatPreferences.DeniedModHashes.Value));

        if (!result.Allowed)
        {
            Reject(peer, result.Reason);
            return;
        }

        _connections.MarkVerified(peer.ConnectionId);
        _messaging.SendToClient(connection, WireCodec.EncodeResult(true, result.Reason));
        MelonLogger.Msg($"{ModInfo.LogPrefix} Verified SteamID {peer.SteamId} (connection {peer.ConnectionId}, {mods.Count} mod(s)).");
    }

    private void Reject(PeerState peer, string reason)
    {
        _connections.Deny(peer);
        if (peer.Connection != null)
        {
            _messaging.SendToClient(peer.Connection, WireCodec.EncodeResult(false, reason));
        }

        _connections.QueueDisconnect(peer);
        MelonLogger.Warning($"{ModInfo.LogPrefix} Rejected SteamID {peer.SteamId} (connection {peer.ConnectionId}): {reason}");
    }
}
