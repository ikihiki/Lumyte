namespace Lumyte.Graphics.Pipelines;

/// <summary>Optionally lowers, never raises, the compiled scratch ceiling for one immutable request.</summary>
public interface IGraphicsPreparationBudget
{
    /// <summary>Gets the nonnegative remaining host/device preparation bytes after caller-owned capture and snapshots.</summary>
    long MaxPreparationBytes { get; }
}
