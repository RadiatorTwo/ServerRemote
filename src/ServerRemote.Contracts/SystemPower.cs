namespace ServerRemote.Contracts;

/// <summary>Power action for the entire system.</summary>
public enum SystemPowerAction
{
    Shutdown,
    Reboot
}

/// <summary>
/// Request for a power action. <see cref="Confirm"/> must be true
/// for the destructive action to be executed.
/// </summary>
public sealed record SystemPowerRequest
{
    public SystemPowerAction Action { get; init; }

    /// <summary>Delay in seconds before execution (allows cancellation).</summary>
    public int DelaySeconds { get; init; } = 15;

    /// <summary>Mandatory confirmation flag to guard against accidental triggering.</summary>
    public bool Confirm { get; init; }
}

public sealed record SystemPowerResultDto
{
    public bool Scheduled { get; init; }
    public string? Message { get; init; }
}
