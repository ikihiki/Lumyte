namespace Lumyte.Graphics.Abstractions;

/// <summary>Indicates that work cannot continue on a lost physical device.</summary>
/// <param name="loss">The preserved backend diagnosis.</param>
public sealed class GraphicsDeviceLostException(GraphicsDeviceLoss loss) : InvalidOperationException(loss.Message)
{
    /// <summary>Gets the preserved diagnosis.</summary>
    public GraphicsDeviceLoss Loss { get; } = loss;
}
