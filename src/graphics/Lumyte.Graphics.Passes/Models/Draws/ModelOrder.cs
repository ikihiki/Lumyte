using System.Numerics;

namespace Lumyte.Graphics.Passes;

// Exact dyadic order labels keep repeated insertion between two draws from exhausting precision.
internal readonly record struct ModelOrder : IComparable<ModelOrder>
{
    private ModelOrder(BigInteger value,int scale)
    {
        while (scale>0 && value.IsEven) { value >>= 1; scale--; }
        Value = value; Scale = scale;
    }
    private BigInteger Value { get; }
    private int Scale { get; }
    public int CompareTo(ModelOrder other)
    { int scale = Math.Max(Scale,other.Scale); return (Value << (scale-Scale)).CompareTo(other.Value << (scale-other.Scale)); }
    public static implicit operator ModelOrder(int value) => new(value,0);
    public static ModelOrder operator +(ModelOrder a,ModelOrder b)
    { int scale = Math.Max(a.Scale,b.Scale); return new((a.Value << (scale-a.Scale))+(b.Value << (scale-b.Scale)),scale); }
    public static ModelOrder operator -(ModelOrder a,ModelOrder b) => a+new ModelOrder(-b.Value,b.Scale);
    public static ModelOrder operator /(ModelOrder a,int divisor)
    { if (divisor!=2) { throw new ArgumentOutOfRangeException(nameof(divisor)); } return new(a.Value,checked(a.Scale+1)); }
    public static bool operator <(ModelOrder a,ModelOrder b) => a.CompareTo(b)<0;
    public static bool operator >(ModelOrder a,ModelOrder b) => a.CompareTo(b)>0;
    public static bool operator <=(ModelOrder a,ModelOrder b) => a.CompareTo(b)<=0;
    public static bool operator >=(ModelOrder a,ModelOrder b) => a.CompareTo(b)>=0;
}
