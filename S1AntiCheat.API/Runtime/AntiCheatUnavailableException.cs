namespace S1AntiCheat.API.Runtime;

/// <summary>
/// Thrown when a consuming mod requires an active S1 Anti-Cheat runtime that is unavailable or too old.
/// </summary>
public sealed class AntiCheatUnavailableException : InvalidOperationException
{
    /// <summary>
    /// Initializes an exception with a user-facing dependency failure message.
    /// </summary>
    /// <param name="message">The dependency failure message.</param>
    public AntiCheatUnavailableException(string message)
        : base(message)
    {
    }
}
