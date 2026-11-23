using Lumyte.Graphics.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal static class GraphCompositionArtifact2D
{
    internal static StageArtifactKey Key { get; } = new(typeof(GraphCompositionArtifact2D), 1, 0);
}
