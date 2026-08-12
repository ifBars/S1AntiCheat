namespace S1AntiCheat.API.Authorization;

/// <summary>
/// Defines a fixed-window action limit enforced by the listen host.
/// </summary>
public readonly struct AntiCheatActionLimit
{
    /// <summary>
    /// Initializes a new action limit.
    /// </summary>
    /// <param name="maximumActions">Maximum accepted actions in the window.</param>
    /// <param name="window">Length of the fixed window.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for non-positive limits or windows.</exception>
    public AntiCheatActionLimit(int maximumActions, TimeSpan window)
    {
        if (maximumActions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumActions), "Maximum actions must be positive.");
        }

        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "The action window must be positive.");
        }

        MaximumActions = maximumActions;
        Window = window;
    }

    /// <summary>
    /// Gets the maximum accepted action count.
    /// </summary>
    public int MaximumActions { get; }

    /// <summary>
    /// Gets the fixed-window duration.
    /// </summary>
    public TimeSpan Window { get; }
}
