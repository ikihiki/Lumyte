namespace Lumyte.Graphics.TwoD;

/// <summary>A stable, generation-checked retained-scene node identifier.</summary>
public readonly record struct NodeId
{
    internal NodeId(int slot, uint generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public bool IsNull => Generation == 0;

    internal int Slot { get; }
    internal uint Generation { get; }
}
