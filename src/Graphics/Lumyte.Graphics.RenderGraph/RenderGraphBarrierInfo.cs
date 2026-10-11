using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Describes a planned memory dependency without retaining its allocation.</summary>
/// <param name="PassIndex">The subsequent live pass; pass count denotes a final export.</param>
/// <param name="ResourceIndex">The resource index in the snapshot.</param>
/// <param name="BeforeState">The preceding texture state.</param>
/// <param name="AfterState">The subsequent texture state.</param>
/// <param name="Before">The preceding scope.</param>
/// <param name="After">The subsequent scope.</param>
public sealed record RenderGraphBarrierInfo(int PassIndex, int ResourceIndex, TextureState BeforeState, TextureState AfterState, BarrierScope Before, BarrierScope After);
