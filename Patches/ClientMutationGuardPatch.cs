using S1AntiCheat.Bootstrap;
using System.Diagnostics;
using System.Reflection;
using MelonLoader;
using S1AntiCheat.Configuration;
#if MONO
using InstanceFinder = FishNet.InstanceFinder;
#else
using InstanceFinder = Il2CppFishNet.InstanceFinder;
#endif

namespace S1AntiCheat.Patches;

internal static class ClientMutationGuardPatch
{
    private static readonly string[] FrameworkAssemblyPrefixes =
    {
        "0Harmony",
        "Il2CppInterop",
        "Il2Cppmscorlib",
        "Il2CppSystem",
        "MelonLoader",
        "Microsoft.",
        "MonoMod.",
        "mscorlib",
        "netstandard",
        "System",
        "UnityEngine"
    };

    internal static bool Prefix(MethodBase __originalMethod)
    {
        if (!AntiCheatPreferences.EnableClientMutationGuards.Value ||
            !InstanceFinder.IsClient ||
            InstanceFinder.IsHost)
        {
            return true;
        }

        Assembly? caller = FindExternalCaller(__originalMethod);
        if (caller == null ||
            caller == __originalMethod.DeclaringType?.Assembly ||
            IsAllowed(caller.GetName().Name))
        {
            return true;
        }

        MelonLogger.Warning(
            $"{ModInfo.LogPrefix} Blocked client mod call from {caller.GetName().Name} into " +
            $"{__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}.");
        return false;
    }

    private static Assembly? FindExternalCaller(MethodBase originalMethod)
    {
        StackFrame[]? frames = new StackTrace(skipFrames: 1, fNeedFileInfo: false).GetFrames();
        if (frames == null)
        {
            return null;
        }

        Assembly guardAssembly = typeof(ClientMutationGuardPatch).Assembly;
        foreach (StackFrame frame in frames)
        {
            MethodBase? method = frame.GetMethod();
            Assembly? assembly = method?.DeclaringType?.Assembly;
            if (assembly == null || assembly == guardAssembly || method == originalMethod)
            {
                continue;
            }

            string assemblyName = assembly.GetName().Name ?? string.Empty;
            if (FrameworkAssemblyPrefixes.Any(prefix =>
                    assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return assembly;
        }

        return null;
    }

    private static bool IsAllowed(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return false;
        }

        return AntiCheatPreferences.AllowedClientMutationAssemblies.Value
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Any(value => string.Equals(value, assemblyName, StringComparison.OrdinalIgnoreCase));
    }
}
