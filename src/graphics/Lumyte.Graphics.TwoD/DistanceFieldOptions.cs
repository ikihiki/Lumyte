namespace Lumyte.Graphics.TwoD;

public readonly record struct DistanceFieldOptions
{
    public DistanceFieldOptions() { }

    public FillRule FillRule { get; init; } = FillRule.NonZero;
    public DistanceFieldEncoding Encoding { get; init; } = DistanceFieldEncoding.SignedDistance;
    public float DistanceRange { get; init; } = 4;

    internal DistanceFieldOptions Validate()
    {
        if (!Enum.IsDefined(FillRule) || !Enum.IsDefined(Encoding))
        {
            throw new ArgumentOutOfRangeException(nameof(FillRule));
        }
        if (!float.IsFinite(DistanceRange) || DistanceRange <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DistanceRange));
        }
        return this;
    }
}
