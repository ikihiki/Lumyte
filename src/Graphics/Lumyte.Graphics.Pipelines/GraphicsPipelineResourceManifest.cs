namespace Lumyte.Graphics.Pipelines;

/// <summary>Captures every externally shared native allocation used by a record plan.</summary>
public sealed class GraphicsPipelineResourceManifest
{
    private readonly PipelineResourceSlot[] _slots;

    internal GraphicsPipelineResourceManifest(IEnumerable<PipelineExternalResource> resources)
    {
        Resources = resources.Select(resource => resource.Snapshot()).ToArray();
        _slots = Enumerable.Range(0, Resources.Length).Select(index => new PipelineResourceSlot(this, index)).ToArray();
    }

    /// <summary>Gets the number of exact external texture/buffer/resource uses.</summary>
    public int Count => _slots.Length;

    internal PipelineExternalResource[] Resources { get; }

    /// <summary>Gets the opaque key of one captured external resource use.</summary>
    /// <param name="index">The zero-based manifest index.</param>
    /// <returns>The immutable manifest-local key.</returns>
    public PipelineResourceSlot GetSlot(int index)
    {
        if ((uint)index >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _slots[index];
    }
}
