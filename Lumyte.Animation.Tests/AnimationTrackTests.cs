using System.Numerics;
using Lumyte.Core.Time;
using Xunit;

using static Lumyte.Animation.AnimationKit;

namespace Lumyte.Animation.Tests;

public sealed class AnimationTrackTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(0.5, 5)]
    [InlineData(1, 10)]
    [InlineData(2, 10)]
    public void TrackSamplesAndClampsItsTimeline(double seconds, float expected)
    {
        var track = Track("Opacity", Interpolators.Float)[
            Keyframe(Duration.Zero, 0f),
            Keyframe(Duration.FromSeconds(1), 10f)
        ];

        var result = track.Sample(Duration.FromSeconds(seconds));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void TrackInterpolatesVectorValues()
    {
        var track = Track("Position", Interpolators.Vector2)[
            Keyframe(Duration.Zero, new Vector2(0f, 20f)),
            Keyframe(Duration.FromSeconds(0.4), Vector2.Zero)
        ];

        var result = track.Sample(Duration.FromSeconds(0.125));

        Assert.Equal(new Vector2(0f, 13.75f), result);
    }

    [Fact]
    public void DiscreteInterpolationHoldsThePreviousValue()
    {
        var track = Track("Visible", Interpolators.Discrete<bool>())[
            Keyframe(Duration.Zero, false),
            Keyframe(Duration.FromSeconds(1), true)
        ];

        var result = track.Sample(Duration.FromSeconds(0.75));

        Assert.False(result);
    }

    [Fact]
    public void TrackRejectsDuplicateKeyframeTimes()
    {
        var track = Track("Opacity", Interpolators.Float);
        var keyframes = new[]
        {
            Keyframe(Duration.Zero, 0f),
            Keyframe(Duration.Zero, 1f),
        };

        var exception = Assert.Throws<ArgumentException>(() => _ = track[keyframes]);

        Assert.Equal("keyframes", exception.ParamName);
        Assert.Contains("strictly increasing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyTrackCannotBeSampled()
    {
        var track = Track("Opacity", Interpolators.Float);

        var exception = Assert.Throws<InvalidOperationException>(() => track.Sample(Duration.Zero));

        Assert.Contains("has no keyframes", exception.Message, StringComparison.Ordinal);
    }
}
