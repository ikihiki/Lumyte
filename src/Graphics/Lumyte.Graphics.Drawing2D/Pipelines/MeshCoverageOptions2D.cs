namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Explicit immutable bounds for mesh coverage preparation.</summary>
/// <param name="MaxCommands">Maximum logical commands in one candidate batch.</param>
/// <param name="MaxWorkingBytes">Conservative CPU and GPU preparation bound.</param>
/// <param name="NumericPoolBytes">Maximum idle numeric stream storage.</param>
public sealed record MeshCoverageOptions2D(int MaxCommands, long MaxWorkingBytes, long NumericPoolBytes)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCommands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxWorkingBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(NumericPoolBytes);
    }
}
