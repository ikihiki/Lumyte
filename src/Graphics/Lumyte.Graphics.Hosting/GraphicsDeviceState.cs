namespace Lumyte.Graphics.Hosting;

/// <summary>Captures a named device generation and its irreversible invalidation watermark.</summary>
public sealed record GraphicsDeviceState
{
    /// <summary>Gets the DI registration name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the monotonic state version.</summary>
    public required ulong Version { get; init; }

    /// <summary>Gets the physical instance generation.</summary>
    public required ulong Epoch { get; init; }

    /// <summary>Gets the service state.</summary>
    public required GraphicsDeviceServiceStatus Status { get; init; }

    /// <summary>Gets the largest irreversibly lost Ready version.</summary>
    public required ulong InvalidatedThroughVersion { get; init; }
}
