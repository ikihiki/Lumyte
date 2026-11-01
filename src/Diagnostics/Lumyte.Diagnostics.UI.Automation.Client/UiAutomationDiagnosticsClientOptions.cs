namespace Lumyte.Diagnostics.UI.Automation.Client;

/// <summary>Defines independent bounded RPC, game job, operation and cleanup deadlines.</summary>
public sealed record UiAutomationDiagnosticsClientOptions
{
    /// <summary>Gets the game job timeout, 100 to 10000 milliseconds.</summary>
    public int JobTimeoutMilliseconds { get; init; } = 5000;

    /// <summary>Gets the RPC timeout, 100 to 30000 milliseconds; Stream is capped at one second.</summary>
    public int RpcTimeoutMilliseconds { get; init; } = 5000;

    /// <summary>Gets the high-level deadline including submission, polling and downloads.</summary>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets the bounded release and cancellation deadline.</summary>
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets the status polling interval, at least 250 milliseconds.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
}
