using System.Security.Cryptography;
using System.Text;
using S1AntiCheat.API.Models;

namespace S1AntiCheat.API.Services;

internal static class ModVerificationPolicy
{
    private static readonly string[] BuiltInRiskyNames =
    {
        "cdxx",
        "legacyblazesmenu",
        "modern cheat menu",
        "modern_cheat_menu",
        "nasty mod",
        "nastymod v2",
        "nastymod_v2",
        "nugzzmenu",
        "ultimate mod menu",
        "ultimatemodmenu",
        "unityexplorer",
        "unityexplorerstandalone"
    };

    private static readonly string[] BuiltInRiskyHashes =
    {
        "0eabd1723f3151449aebc1f4ff09f2db88fe8c29a61729c8d70a443116322b68",
        "38a838a417003f71edd1c815afd5da474c7547f751b629a3ff66a23bff253d39",
        "3c5d9a073fbf0054dcb008cba9d708129aaefc7130efc62b864bc1e50f377a92",
        "56b67630b08fe2a253fe2fdc7ead7e7d049da09c74447d5043fb3d594836d8de",
        "85b64ace2294418cf9b6b33263e3b9b0e567938d983f5bd6005c34e38335a745",
        "c88933a451e7b7c1a96a10093f89a222ba414899311ee088264eebc97e9c9e7e"
    };

    internal static ModVerificationResult Evaluate(
        IReadOnlyCollection<ModDescriptor> clientMods,
        ModVerificationMode mode,
        string hostFingerprint,
        ISet<string> ignoredNames,
        ISet<string> deniedNames,
        ISet<string> deniedHashes)
    {
        foreach (ModDescriptor mod in clientMods)
        {
            if (ignoredNames.Contains(NormalizeName(mod.AssemblyName)) ||
                ignoredNames.Contains(NormalizeName(mod.DisplayName)))
            {
                continue;
            }

            if (deniedHashes.Contains(NormalizeHash(mod.Sha256)))
            {
                return new ModVerificationResult(false, $"Blocked mod hash reported for {Describe(mod)}.");
            }

            if (mode != ModVerificationMode.RequiredOnly &&
                BuiltInRiskyHashes.Contains(NormalizeHash(mod.Sha256), StringComparer.OrdinalIgnoreCase))
            {
                return new ModVerificationResult(false, $"Known mod-menu build reported for {Describe(mod)}.");
            }

            if (MatchesName(mod, deniedNames))
            {
                return new ModVerificationResult(false, $"Blocked mod reported: {Describe(mod)}.");
            }

            if (mode != ModVerificationMode.RequiredOnly && BuiltInRiskyNames.Any(name => MatchesName(mod, name)))
            {
                return new ModVerificationResult(false, $"Known cheat or runtime explorer reported: {Describe(mod)}.");
            }
        }

        if (mode == ModVerificationMode.MatchHost)
        {
            string clientFingerprint = ComputeFingerprint(clientMods, ignoredNames);
            if (!string.Equals(clientFingerprint, hostFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return new ModVerificationResult(false, "The client mod manifest does not match the host.");
            }
        }

        return new ModVerificationResult(true, "Client anti-cheat verification passed.");
    }

    internal static string ComputeFingerprint(
        IEnumerable<ModDescriptor> mods,
        ISet<string>? ignoredNames = null)
    {
        IEnumerable<ModDescriptor> included = mods.Where(mod =>
            ignoredNames == null ||
            !ignoredNames.Contains(NormalizeName(mod.AssemblyName)) &&
            !ignoredNames.Contains(NormalizeName(mod.DisplayName)));

        string canonical = string.Join(
            "\n",
            included
                .OrderBy(mod => NormalizeName(mod.AssemblyName), StringComparer.Ordinal)
                .ThenBy(mod => NormalizeName(mod.DisplayName), StringComparer.Ordinal)
                .Select(mod => string.Join(
                    "|",
                    NormalizeName(mod.AssemblyName),
                    NormalizeName(mod.DisplayName),
                    mod.Version.Trim(),
                    NormalizeHash(mod.Sha256))));

        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    internal static HashSet<string> ParseNames(string? values)
    {
        return ParseSet(values, NormalizeName);
    }

    internal static HashSet<string> ParseHashes(string? values)
    {
        return ParseSet(values, NormalizeHash);
    }

    private static HashSet<string> ParseSet(string? values, Func<string, string> normalize)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(values))
        {
            return result;
        }

        foreach (string value in values.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = normalize(value);
            if (normalized.Length > 0)
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    private static bool MatchesName(ModDescriptor mod, IEnumerable<string> names)
    {
        return names.Any(name => MatchesName(mod, name));
    }

    private static bool MatchesName(ModDescriptor mod, string name)
    {
        string normalized = NormalizeName(name);
        return NormalizeName(mod.AssemblyName) == normalized || NormalizeName(mod.DisplayName) == normalized;
    }

    private static string Describe(ModDescriptor mod)
    {
        return mod.DisplayName.Length > 0 ? mod.DisplayName : mod.AssemblyName;
    }

    private static string NormalizeName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeHash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }
}
