using System.Reflection;
using MelonLoader;
using S1AntiCheat.Configuration;
#if MONO
using NetworkBehaviour = FishNet.Object.NetworkBehaviour;
using NetworkConnection = FishNet.Connection.NetworkConnection;
#else
using NetworkBehaviour = Il2CppFishNet.Object.NetworkBehaviour;
using NetworkConnection = Il2CppFishNet.Connection.NetworkConnection;
#endif

namespace S1AntiCheat.Patches;

internal static class SensitiveRpcOwnershipPatch
{
    internal static bool Prefix(
        NetworkBehaviour __instance,
        NetworkConnection conn,
        MethodBase __originalMethod)
    {
        if (!AntiCheatPreferences.EnableRpcOwnershipGuards.Value ||
            conn == null ||
            conn.IsLocalClient ||
            __instance.OwnerMatches(conn))
        {
            return true;
        }

        Runtime.PeerState peer = PatchContext.Connections.Attach(conn);
        PatchContext.Connections.Deny(peer);
        if (AntiCheatPreferences.DisconnectOnExploitAttempt.Value)
        {
            PatchContext.Connections.QueueDisconnect(peer);
        }

        string target = $"{__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}";
        MelonLogger.Warning(
            $"{Constants.LogPrefix} Blocked RPC ownership violation from connection {conn.ClientId} " +
            $"(SteamID {peer.SteamId}) targeting {target}.");
        return false;
    }
}
