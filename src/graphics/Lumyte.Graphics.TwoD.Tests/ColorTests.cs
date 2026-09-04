namespace Lumyte.Graphics.TwoD.Tests;

public sealed class ColorTests
{
    [Fact]
    public void FromSrgbConvertsColorChannelsToLinearSpace()
    {
        Color color = Color.FromSrgb(0.5f, 0.25f, 1, 0.75f);

        Assert.InRange(color.Red, 0.2140f, 0.2141f);
        Assert.InRange(color.Green, 0.0508f, 0.0509f);
        Assert.Equal(1, color.Blue);
        Assert.Equal(0.75f, color.Alpha);
    }
}
