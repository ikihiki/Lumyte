namespace Lumyte.Graphics.TwoD;

/// <summary>A generation-checked reference to one R8 atlas region.</summary>
public readonly struct DistanceField : IEquatable<DistanceField>
{
    internal DistanceField(DistanceFieldAtlas owner, int slot, uint generation)
    {
        Owner = owner;
        Slot = slot;
        Generation = generation;
    }

    public bool IsNull => Owner is null;
    public Rect AtlasRegion
    {
        get
        {
            DistanceFieldEntry entry = Require();
            return new(entry.Region.X, entry.Region.Y, entry.Region.Width, entry.Region.Height);
        }
    }
    public float DistanceRange => Require().DistanceRange;
    public DistanceFieldEncoding Encoding => Require().Encoding;

    internal DistanceFieldAtlas? Owner { get; }
    internal int Slot { get; }
    internal uint Generation { get; }

    public bool Equals(DistanceField other)
        => ReferenceEquals(Owner, other.Owner) && Slot == other.Slot && Generation == other.Generation;

    public override bool Equals(object? value) => value is DistanceField other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Owner, Slot, Generation);
    public static bool operator ==(DistanceField left, DistanceField right) => left.Equals(right);
    public static bool operator !=(DistanceField left, DistanceField right) => !left.Equals(right);

    private DistanceFieldEntry Require()
        => Owner?.Require(this)
            ?? throw new InvalidOperationException("Distance field is null.");
}
