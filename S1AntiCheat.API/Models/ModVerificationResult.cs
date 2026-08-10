namespace S1AntiCheat.API.Models;

internal enum ModVerificationMode
{
    RequiredOnly,
    BlockKnownRisky,
    MatchHost
}

internal readonly struct ModVerificationResult
{
    internal ModVerificationResult(bool allowed, string reason)
    {
        Allowed = allowed;
        Reason = reason ?? string.Empty;
    }

    internal bool Allowed { get; }

    internal string Reason { get; }
}
