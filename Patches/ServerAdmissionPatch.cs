using S1AntiCheat.Bootstrap;
using MelonLoader;
using S1AntiCheat.API.Peers;
using S1AntiCheat.Configuration;
using S1AntiCheat.Runtime;
#if MONO
using NetworkConnection = FishNet.Connection.NetworkConnection;
using RemoteConnectionState = FishNet.Transporting.RemoteConnectionState;
using RemoteConnectionStateArgs = FishNet.Transporting.RemoteConnectionStateArgs;
using ServerManager = FishNet.Managing.Server.ServerManager;
using SteamUser = Steamworks.SteamUser;
#else
using NetworkConnection = Il2CppFishNet.Connection.NetworkConnection;
using RemoteConnectionState = Il2CppFishNet.Transporting.RemoteConnectionState;
using RemoteConnectionStateArgs = Il2CppFishNet.Transporting.RemoteConnectionStateArgs;
using ServerManager = Il2CppFishNet.Managing.Server.ServerManager;
using SteamUser = Il2CppSteamworks.SteamUser;
#endif

namespace S1AntiCheat.Patches;

internal static class ServerAdmissionPatch
{
    internal static bool Prefix(ServerManager __instance, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            PatchContext.Connections.Remove(args.ConnectionId);
            return true;
        }

        if (args.ConnectionState != RemoteConnectionState.Started)
        {
            return true;
        }

        string? transportAddress = null;
        try
        {
            transportAddress = __instance.NetworkManager.TransportManager.Transport.GetConnectionAddress(args.ConnectionId);
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"{ModInfo.LogPrefix} Could not read transport identity: {exception.Message}");
        }

        AdmissionPolicy.TryParseSteamId(transportAddress, out ulong transportSteamId);
        if (!AntiCheatPreferences.EnableAdmissionGate.Value)
        {
            PatchContext.Connections.Admit(args.ConnectionId, transportSteamId);
            return true;
        }

        bool lobbyAvailable = LobbyAccess.TryGetMemberIds(out string[] lobbyMemberIds);
        AdmissionDecision decision = AdmissionPolicy.Evaluate(
            args.ConnectionId,
            transportAddress,
            SteamUser.GetSteamID().m_SteamID,
            lobbyAvailable,
            lobbyMemberIds,
            LobbyAccess.IsImmediateSteamFriend(transportSteamId),
            AntiCheatPreferences.TrustSteamFriendsInLobby.Value,
            AntiCheatPreferences.TrustAllCurrentLobbyMembers.Value,
            PatchContext.Connections.ExplicitlyAllowedSteamIds,
            PatchContext.Connections.DeniedSteamIds,
            AntiCheatPreferences.FailClosedWhenLobbyUnavailable.Value);

        if (decision.Allowed)
        {
            PatchContext.Connections.Admit(args.ConnectionId, decision.SteamId);
            if (decision.Reason != AdmissionReason.LocalHost)
            {
                MelonLogger.Msg(
                    $"{ModInfo.LogPrefix} Admitted SteamID {decision.SteamId} " +
                    $"({decision.Reason}, connection {args.ConnectionId}).");
            }

            return true;
        }

        PeerState peer = PatchContext.Connections.Begin(args.ConnectionId, decision.SteamId);
        PatchContext.Connections.Deny(peer);
        MelonLogger.Warning(
            $"{ModInfo.LogPrefix} Rejected SteamID {decision.SteamId} " +
            $"({decision.Reason}, connection {args.ConnectionId}).");

        try
        {
            __instance.NetworkManager.TransportManager.Transport.StopConnection(args.ConnectionId, true);
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"{ModInfo.LogPrefix} Failed to close rejected connection: {exception}");
        }

        return false;
    }
}
