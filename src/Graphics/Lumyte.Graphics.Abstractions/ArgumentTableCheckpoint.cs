namespace Lumyte.Graphics.Abstractions;

/// <summary>An opaque unpublished collection boundary; default is invalid.</summary>
public readonly struct ArgumentTableCheckpoint
{
    internal ArgumentTableCheckpoint(ArgumentTablePlan owner, int count)
    {
        Owner = owner;
        Count = count;
    }

    internal ArgumentTablePlan? Owner { get; }

    internal int Count { get; }
}
