namespace Lumyte.Graphics.Drawing2D;

/// <summary>Reports actual preparation-budget participation without identifying a rendering algorithm.</summary>
internal interface IPreparedWorkingBudget2D
{
    /// <summary>Gets a value indicating whether this prepared owner requires cumulative admission.</summary>
    bool EnforcesWorkingBudget { get; }
}
