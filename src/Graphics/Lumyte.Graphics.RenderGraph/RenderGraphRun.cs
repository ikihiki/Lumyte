namespace Lumyte.Graphics.RenderGraph;

/// <summary>Identifies a builder and one compilation independently of other graphs and frames.</summary>
/// <param name="GraphId">The stable builder identity.</param>
/// <param name="RunId">The identity shared by compilation and recording notifications.</param>
/// <param name="Name">The graph's diagnostic name.</param>
/// <param name="FrameId">The caller's frame number, or zero when unspecified.</param>
public readonly record struct RenderGraphRun(Guid GraphId, Guid RunId, string Name, long FrameId);
