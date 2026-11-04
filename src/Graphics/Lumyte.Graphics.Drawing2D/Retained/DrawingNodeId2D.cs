namespace Lumyte.Graphics.Drawing2D;

/// <summary>An opaque scene-owned placement identity with a slot generation.</summary>
public readonly struct DrawingNodeId2D : IEquatable<DrawingNodeId2D>
{
    internal DrawingNodeId2D(object owner, int slot, ulong generation)
    {
        Owner = owner;
        Slot = slot;
        Generation = generation;
    }

    internal object? Owner { get; }

    internal int Slot { get; }

    internal ulong Generation { get; }

    /// <summary>Compares placement identities.</summary>
    /// <param name="left">The first identity.</param>
    /// <param name="right">The second identity.</param>
    /// <returns>Whether the identities match.</returns>
    public static bool operator ==(DrawingNodeId2D left, DrawingNodeId2D right) => left.Equals(right);

    /// <summary>Compares placement identities.</summary>
    /// <param name="left">The first identity.</param>
    /// <param name="right">The second identity.</param>
    /// <returns>Whether the identities differ.</returns>
    public static bool operator !=(DrawingNodeId2D left, DrawingNodeId2D right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(DrawingNodeId2D other) => ReferenceEquals(Owner, other.Owner) && Slot == other.Slot && Generation == other.Generation;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DrawingNodeId2D other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Owner, Slot, Generation);
}
