namespace Lumyte.Graphics.TwoD;

public readonly record struct Brush(Color Color)
{
    public static Brush Solid(Color color) => new(color.Validate());

    public Brush Validate()
    {
        Color.Validate();
        return this;
    }
}
