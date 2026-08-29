using System.Numerics;
using Lumyte.Core.Time;
using Xunit;

using static Lumyte.Animation.AnimationKit;

namespace Lumyte.Animation.Tests;

public sealed class AnimationClipTests
{
    [Fact]
    public void ClipSamplesTypedTracksFromTheIndexerDsl()
    {
        var opacity = Track(Channel<float>("Opacity"), Interpolators.Float)[
            Keyframe(Duration.Zero, 0f),
            Keyframe(Duration.FromSeconds(0.25), 1f)
        ];
        var position = Track(Channel<Vector2>("Position"), Interpolators.Vector2)[
            Keyframe(Duration.Zero, new Vector2(0f, 20f)),
            Keyframe(Duration.FromSeconds(0.4), Vector2.Zero)
        ];
        var clip = Clip("Entrance")[opacity, position];

        var sample = clip.Sample(Duration.FromSeconds(0.125));
        var result = new
        {
            Opacity = sample.Get(opacity),
            Position = sample.Get(position),
            Duration = clip.Duration,
        };

        var expected = new
        {
            Opacity = 0.5f,
            Position = new Vector2(0f, 13.75f),
            Duration = Duration.FromSeconds(0.4),
        };
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClipRejectsDuplicateTrackNames()
    {
        var first = Track(Channel<float>("Value"), Interpolators.Float)[Keyframe(Duration.Zero, 0f)];
        var second = Track(Channel<float>("Value"), Interpolators.Float)[Keyframe(Duration.Zero, 1f)];
        var clip = Clip("Duplicate names");

        var exception = Assert.Throws<ArgumentException>(() => _ = clip[first, second]);

        Assert.Equal("tracks", exception.ParamName);
        Assert.Contains("must be unique", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SampleRejectsATrackFromAnotherClip()
    {
        var included = Track(Channel<float>("Included"), Interpolators.Float)[Keyframe(Duration.Zero, 1f)];
        var external = Track(Channel<float>("External"), Interpolators.Float)[Keyframe(Duration.Zero, 2f)];
        var sample = Clip("One track")[included].Sample(Duration.Zero);

        var exception = Assert.Throws<ArgumentException>(() => sample.Get(external));

        Assert.Equal("channel", exception.ParamName);
        Assert.Contains("does not belong", exception.Message, StringComparison.Ordinal);
    }
}
