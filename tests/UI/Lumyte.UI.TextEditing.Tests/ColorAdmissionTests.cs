using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.UI.TextEditing.Tests;

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
            var bad = new Rgba<float>(channels[0], channels[1], channels[2], channels[3]);
            ImeCandidateStyle original = ImeCandidateStyle.Default;
            Rgba<float> good = new(0, 0, 0, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ImeCandidateStyle(bad, good, good, good));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ImeCandidateStyle(good, bad, good, good));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ImeCandidateStyle(good, good, bad, good));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ImeCandidateStyle(good, good, good, bad));
            Assert.Throws<ArgumentOutOfRangeException>(() => original with { Background = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => original with { Foreground = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => original with { SelectedBackground = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => original with { SelectedForeground = bad });
            Assert.Equal(ImeCandidateStyle.Default, original);
        }
    }
}
