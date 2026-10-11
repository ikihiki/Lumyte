namespace Lumyte.Graphics.RenderGraph;

/// <summary>Contains fixed-size compile statistics without detailed diagnostic descriptions.</summary>
/// <param name="LivePasses">The number of recorded passes.</param>
/// <param name="CulledPasses">The number of removed passes.</param>
/// <param name="Resources">The number of declared resources.</param>
/// <param name="TransientResources">The number of declared temporary resources, including unused ones.</param>
/// <param name="Dependencies">The number of ordering edges between live passes.</param>
/// <param name="Barriers">The number of planned barriers, including exports.</param>
public readonly record struct RenderGraphStatistics(int LivePasses, int CulledPasses, int Resources, int TransientResources, int Dependencies, int Barriers);
