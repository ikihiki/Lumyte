namespace Lumyte.Graphics.Pipelines;

/// <summary>An opaque key for one exact external resource use in a prepared manifest.</summary>
public sealed class PipelineResourceSlot
{
    internal PipelineResourceSlot(GraphicsPipelineResourceManifest owner, int index)
    {
        Owner = owner;
        Index = index;
    }

    internal GraphicsPipelineResourceManifest Owner { get; }

    internal int Index { get; }
}
