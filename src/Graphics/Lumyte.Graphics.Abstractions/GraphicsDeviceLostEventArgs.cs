namespace Lumyte.Graphics.Abstractions;

/// <summary>Reports an irreversible device loss.</summary>
/// <param name="loss">The backend diagnosis.</param>
public sealed class GraphicsDeviceLostEventArgs(GraphicsDeviceLoss loss) : EventArgs
{
    /// <summary>Gets the preserved diagnosis.</summary>
    public GraphicsDeviceLoss Loss { get; } = loss;
}
