using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks color admission before public state publication.</summary>
public sealed class ColorAdmissionTests
{
    /// <summary>All channels reject raw finite non-unit values without changing old state.</summary>
    /// <param name="invalid">The finite component outside the unit interval.</param>
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void RejectsInvalidComponents(float invalid)
    {
        for (int component = 0; component < 4; component++)
        {
            float[] channels = [0, 0, 0, 1];
            channels[component] = invalid;
            var bad = new SrgbRgba<float>(channels[0], channels[1], channels[2], channels[3]);
            var original = new UiApplicationOptions();
            Assert.Throws<ArgumentOutOfRangeException>(() => new UiApplicationOptions { Background = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => original with { Background = bad });
            Assert.Equal(new SrgbRgba<float>(1, 1, 1, 1), original.Background);
        }
    }
}
