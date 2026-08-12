using System.Text;
using S1AntiCheat.API.Verification;
using S1AntiCheat.Bootstrap;

namespace S1AntiCheat.Networking;

internal static class WireCodec
{
    private const string ProtocolVersion = "1";

    private const int MaximumMods = 256;
    private const int MaximumPayloadLength = 256 * 1024;

    internal static string EncodeChallenge(string nonce, int timeoutSeconds, string hostFingerprint)
    {
        return string.Join("|", "C", ProtocolVersion, Encode(ModInfo.Version), Encode(nonce), timeoutSeconds, Encode(hostFingerprint));
    }

    internal static bool TryDecodeChallenge(string payload, out string nonce)
    {
        nonce = string.Empty;
        string[] parts = Split(payload);
        return parts.Length == 6 && parts[0] == "C" && parts[1] == ProtocolVersion &&
               TryDecode(parts[3], out nonce) && nonce.Length > 0;
    }

    internal static string EncodeReport(string nonce, IReadOnlyList<ModDescriptor> mods)
    {
        var lines = new List<string>(mods.Count + 1)
        {
            string.Join("|", "R", ProtocolVersion, Encode(ModInfo.Version), Encode(nonce), mods.Count)
        };

        foreach (ModDescriptor mod in mods)
        {
            lines.Add(string.Join(
                "|",
                "M",
                Encode(mod.AssemblyName),
                Encode(mod.DisplayName),
                Encode(mod.Version),
                Encode(mod.Author),
                Encode(mod.Sha256)));
        }

        return string.Join("\n", lines);
    }

    internal static bool TryDecodeReport(
        string payload,
        out string runtimeVersion,
        out string nonce,
        out IReadOnlyList<ModDescriptor> mods)
    {
        runtimeVersion = string.Empty;
        nonce = string.Empty;
        mods = Array.Empty<ModDescriptor>();
        if (string.IsNullOrEmpty(payload) || payload.Length > MaximumPayloadLength)
        {
            return false;
        }

        string[] lines = payload.Split('\n');
        string[] header = lines.Length > 0 ? Split(lines[0]) : Array.Empty<string>();
        if (header.Length != 5 || header[0] != "R" || header[1] != ProtocolVersion ||
            !TryDecode(header[2], out runtimeVersion) || !TryDecode(header[3], out nonce) ||
            !int.TryParse(header[4], out int count) || count < 0 || count > MaximumMods || lines.Length != count + 1)
        {
            return false;
        }

        var decoded = new List<ModDescriptor>(count);
        for (int index = 1; index < lines.Length; index++)
        {
            string[] fields = Split(lines[index]);
            if (fields.Length != 6 || fields[0] != "M" ||
                !TryDecode(fields[1], out string assemblyName) ||
                !TryDecode(fields[2], out string displayName) ||
                !TryDecode(fields[3], out string version) ||
                !TryDecode(fields[4], out string author) ||
                !TryDecode(fields[5], out string sha256))
            {
                return false;
            }

            decoded.Add(new ModDescriptor(assemblyName, displayName, version, author, sha256));
        }

        mods = decoded;
        return true;
    }

    internal static string EncodeResult(bool allowed, string message)
    {
        return string.Join("|", "A", allowed ? "1" : "0", Encode(message));
    }

    internal static bool TryDecodeResult(string payload, out bool allowed, out string message)
    {
        allowed = false;
        message = string.Empty;
        string[] parts = Split(payload);
        if (parts.Length != 3 || parts[0] != "A" || !TryDecode(parts[2], out message))
        {
            return false;
        }

        allowed = parts[1] == "1";
        return allowed || parts[1] == "0";
    }

    private static string[] Split(string value)
    {
        return value.Split('|');
    }

    private static string Encode(string? value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static bool TryDecode(string value, out string decoded)
    {
        decoded = string.Empty;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
