namespace Lumyte.Graphics.Planning;
/// <summary>Declares plan range.</summary>
/// <param name = "Offset">The offset.</param>
/// <param name = "Count">The count.</param>
public readonly record struct PlanRange(ulong Offset, ulong Count)
{
    /// <summary>Gets end.</summary>
    public ulong End => checked(Offset + Count);

    /// <summary>Declares overlaps.</summary>
    /// <param name = "other">The other.</param>
    /// <returns>The declared result.</returns>
    public bool Overlaps(PlanRange other) => Offset < other.End && other.Offset < End;

    /// <summary>Declares contains.</summary>
    /// <param name = "other">The other.</param>
    /// <returns>The declared result.</returns>
    public bool Contains(PlanRange other) => Offset <= other.Offset && End >= other.End;
}
