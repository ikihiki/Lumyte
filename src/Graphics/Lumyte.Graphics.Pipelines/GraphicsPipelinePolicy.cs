namespace Lumyte.Graphics.Pipelines;

/// <summary>Defines explicit finite graphics execution ceilings.</summary>
public sealed record GraphicsPipelinePolicy
{
    /// <summary>Initializes a new instance of the <see cref="GraphicsPipelinePolicy"/> class.</summary>
    /// <param name="maxScratchBytes">The nonnegative scratch ceiling.</param>
    /// <param name="maxTransferBytes">The nonnegative transfer ceiling.</param>
    /// <param name="maxReadbackBytes">The nonnegative readback ceiling.</param>
    /// <param name="maxEpochs">The positive epoch ceiling.</param>
    public GraphicsPipelinePolicy(long maxScratchBytes, long maxTransferBytes, long maxReadbackBytes, int maxEpochs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxScratchBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTransferBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReadbackBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEpochs);
        MaxScratchBytes = maxScratchBytes;
        MaxTransferBytes = maxTransferBytes;
        MaxReadbackBytes = maxReadbackBytes;
        MaxEpochs = maxEpochs;
    }

    /// <summary>Gets the explicit immutable upload retention policy, or null for fresh transfers.</summary>
    public GraphicsImmutableUploadPolicy? ImmutableUploads { get; init; }

    /// <summary>Gets the scratch ceiling.</summary>
    public long MaxScratchBytes { get; }

    /// <summary>Gets the transfer ceiling.</summary>
    public long MaxTransferBytes { get; }

    /// <summary>Gets the readback ceiling.</summary>
    public long MaxReadbackBytes { get; }

    /// <summary>Gets the epoch ceiling.</summary>
    public int MaxEpochs { get; }
}
