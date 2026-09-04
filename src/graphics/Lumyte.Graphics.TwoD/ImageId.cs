namespace Lumyte.Graphics.TwoD;

public readonly record struct ImageId(ulong Value)
{
    public bool IsNull => Value == 0;
}
