using Lumyte.Core.Time;

using Xunit;

using static Lumyte.Animation.AnimationKit;

namespace Lumyte.Animation.Tests;

public sealed class AnimationTimelineTests
{
    [Fact]
    public void SequenceSamplesChildrenInOrder()
    {
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        var sequence = new SequenceTimeline(
            CreateClip("First", opacity, 0f, 1f),
            CreateClip("Second", opacity, 1f, 3f));

        float value = sequence.Sample(Duration.FromSeconds(1.5)).Get(opacity);

        Assert.Equal(2f, value);
        Assert.Equal(Duration.FromSeconds(2), sequence.Duration);
    }

    [Fact]
    public void ParallelCombinesIndependentChannels()
    {
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        AnimationChannel<float> scale = Channel<float>("Scale");
        var parallel = new ParallelTimeline(
            CreateClip("Fade", opacity, 0f, 1f),
            CreateClip("Grow", scale, 1f, 3f));

        AnimationSample sample = parallel.Sample(Duration.FromSeconds(0.5));

        Assert.Equal(0.5f, sample.Get(opacity));
        Assert.Equal(2f, sample.Get(scale));
    }

    [Fact]
    public void CrossfadeUsesTypedBlendOperations()
    {
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        AnimationClip source = CreateClip("Source", opacity, 0f, 1f);
        AnimationClip destination = CreateClip("Destination", opacity, 1f, 3f);
        var blend = new AnimationBlend().Use(opacity, Interpolators.Float);
        var crossfade = new CrossfadeTimeline(
            source.Sample(Duration.FromSeconds(0.5)),
            destination,
            Duration.FromSeconds(1),
            blend);

        float value = crossfade.Sample(Duration.FromSeconds(0.5)).Get(opacity);

        Assert.Equal(1.25f, value);
    }

    [Fact]
    public void DelayHoldsTheInitialValueBeforeAdvancing()
    {
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        DelayTimeline delayed = Delay(
            CreateClip("Fade", opacity, 0f, 1f),
            Duration.FromSeconds(0.5));

        float held = delayed.Sample(Duration.FromSeconds(0.25)).Get(opacity);
        float advanced = delayed.Sample(Duration.FromSeconds(0.75)).Get(opacity);

        Assert.Equal(0f, held);
        Assert.Equal(0.25f, advanced);
    }

    [Fact]
    public void FiniteRepeatEndsAtTheChildEndpoint()
    {
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        RepeatTimeline repeated = Repeat(CreateClip("Fade", opacity, 0f, 1f), 2);

        float secondCycle = repeated.Sample(Duration.FromSeconds(1.5)).Get(opacity);
        float endpoint = repeated.Sample(Duration.FromSeconds(2)).Get(opacity);

        Assert.Equal(0.5f, secondCycle);
        Assert.Equal(1f, endpoint);
        Assert.Equal(Duration.FromSeconds(2), repeated.Duration);
    }

    [Fact]
    public void ReverseSamplesTheChildBackwards()
    {
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        ReverseTimeline reversed = Reverse(CreateClip("Fade", opacity, 0f, 1f));

        float start = reversed.Sample(Duration.Zero).Get(opacity);
        float quarter = reversed.Sample(Duration.FromSeconds(0.25)).Get(opacity);

        Assert.Equal(1f, start);
        Assert.Equal(0.75f, quarter);
    }

    private static AnimationClip CreateClip(
        string name,
        AnimationChannel<float> channel,
        float from,
        float to)
    {
        AnimationTrack<float> track = Track(channel, Interpolators.Float)[
            Keyframe(Duration.Zero, from),
            Keyframe(Duration.FromSeconds(1), to)
        ];
        return Clip(name)[track];
    }
}
