namespace Lumyte.Graphics.Drawing2D;

/// <summary>Bounds one preparation's conservatively owned CPU and GPU working memory.</summary>
public sealed record DrawingRendererOptions2D
{
    /// <summary>Gets the positive working-byte budget; defaults to 256 MiB, independently of idle retention.</summary>
    public long MaxWorkingBytes { get; init; } = 256L * 1024 * 1024;

    internal DrawingRendererOptions2D Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxWorkingBytes);
        return this with { };
    }
}
