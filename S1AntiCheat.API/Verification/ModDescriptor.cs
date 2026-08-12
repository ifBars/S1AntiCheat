namespace S1AntiCheat.API.Verification;

internal sealed class ModDescriptor
{
    internal ModDescriptor(string assemblyName, string displayName, string version, string author, string sha256)
    {
        AssemblyName = Normalize(assemblyName);
        DisplayName = Normalize(displayName);
        Version = Normalize(version);
        Author = Normalize(author);
        Sha256 = Normalize(sha256).ToLowerInvariant();
    }

    internal string AssemblyName { get; }

    internal string DisplayName { get; }

    internal string Version { get; }

    internal string Author { get; }

    internal string Sha256 { get; }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
