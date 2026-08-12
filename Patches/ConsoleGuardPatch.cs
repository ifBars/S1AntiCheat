using S1AntiCheat.Bootstrap;
using System.Reflection;
using MelonLoader;
#if MONO
using InstanceFinder = FishNet.InstanceFinder;
#else
using InstanceFinder = Il2CppFishNet.InstanceFinder;
#endif

namespace S1AntiCheat.Patches;

internal static class ConsoleGuardPatch
{
    internal static bool Prefix(MethodBase __originalMethod)
    {
        if (InstanceFinder.IsHost)
        {
            return true;
        }

        MelonLogger.Warning(
            $"{ModInfo.LogPrefix} Blocked client-side game console execution: " +
            $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}.");
        return false;
    }
}
