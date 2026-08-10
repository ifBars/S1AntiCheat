using System.Reflection;
using System.Security.Cryptography;
using MelonLoader;
using S1AntiCheat.API.Models;

namespace S1AntiCheat.Services;

internal sealed class ModManifestService
{
    internal IReadOnlyList<ModDescriptor> Build()
    {
        var mods = new List<ModDescriptor>();
        IReadOnlyList<MelonBase>? registeredMelons = MelonMod.RegisteredMelons;
        if (registeredMelons == null)
        {
            return mods;
        }

        foreach (MelonBase melon in registeredMelons)
        {
            try
            {
                if (melon == null)
                {
                    continue;
                }

                Assembly? assembly = melon.MelonAssembly?.Assembly;
                if (assembly == null)
                {
                    continue;
                }

                string assemblyName = assembly.GetName().Name ?? string.Empty;
                MelonInfoAttribute? info = melon.Info;
                mods.Add(new ModDescriptor(
                    assemblyName,
                    info?.Name ?? melon.MelonTypeName ?? assemblyName,
                    info?.Version ?? assembly.GetName().Version?.ToString() ?? string.Empty,
                    info?.Author ?? string.Empty,
                    ResolveSha256(melon)));
            }
            catch (Exception exception)
            {
                MelonLogger.Warning($"{Constants.LogPrefix} Could not describe a loaded mod: {exception.Message}");
            }
        }

        return mods;
    }

    private static string ResolveSha256(MelonBase melon)
    {
        string? location = melon.MelonAssembly?.Location;
        if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
        {
            try
            {
                using FileStream stream = File.OpenRead(location);
                using SHA256 sha256 = SHA256.Create();
                return BitConverter.ToString(sha256.ComputeHash(stream))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
            catch (Exception exception)
            {
                MelonLogger.Warning($"{Constants.LogPrefix} Could not hash {Path.GetFileName(location)}: {exception.Message}");
            }
        }

        return melon.MelonAssembly?.Hash?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
