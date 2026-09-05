using Lumyte.Core.Time;

using Xunit;

using static Lumyte.Animation.AnimationKit;

namespace Lumyte.Animation.Tests;

public sealed class AnimationPlayerTests
{
    [Fact]
    public void PlayerAppliesInitialAndAdvancedSamples()
    {
        var clock = new ManualClock();
        var values = new List<float>();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        AnimationClip clip = CreateClip(opacity);
        var target = new AnimationTarget().Bind(opacity, values.Add);
        var player = new AnimationPlayer(clock);

        player.Play(clip, target);
        clock.Advance(Duration.FromSeconds(0.5));
        player.Update();

        Assert.Equal([0f, 0.5f], values);
    }

    [Fact]
    public void DelayedPlaybackDoesNotWriteBeforeItsStartTime()
    {
        var clock = new ManualClock();
        var values = new List<float>();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        var target = new AnimationTarget().Bind(opacity, values.Add);
        var player = new AnimationPlayer(clock);
        player.Play(CreateClip(opacity), target, new PlaybackOptions
        {
            Delay = Duration.FromSeconds(1),
        });

        clock.Advance(Duration.FromSeconds(0.5));
        player.Update();

        Assert.Empty(values);

        clock.Advance(Duration.FromSeconds(0.5));
        player.Update();

        Assert.Equal([0f], values);
    }

    [Fact]
    public void PausedPlaybackResumesFromTheHeldPosition()
    {
        var clock = new ManualClock();
        var values = new List<float>();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        var target = new AnimationTarget().Bind(opacity, values.Add);
        var player = new AnimationPlayer(clock);
        PlaybackHandle playback = player.Play(CreateClip(opacity), target);
        clock.Advance(Duration.FromSeconds(0.25));
        player.Update();

        playback.Pause();
        clock.Advance(Duration.FromSeconds(0.5));
        player.Update();
        playback.Resume();
        clock.Advance(Duration.FromSeconds(0.25));
        player.Update();

        Assert.Equal([0f, 0.25f, 0.5f], values);
    }

    [Fact]
    public void SeekAppliesTheRequestedPositionImmediately()
    {
        var clock = new ManualClock();
        var values = new List<float>();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        var target = new AnimationTarget().Bind(opacity, values.Add);
        var player = new AnimationPlayer(clock);
        PlaybackHandle playback = player.Play(CreateClip(opacity), target);
        playback.Pause();

        playback.Seek(Duration.FromSeconds(0.75));

        Assert.Equal(0.75f, values[^1]);
        Assert.Equal(PlaybackState.Paused, playback.State);
    }

    [Fact]
    public void OncePlaybackCompletesAtItsEndpointOnlyOnce()
    {
        var clock = new ManualClock();
        var values = new List<float>();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        var target = new AnimationTarget().Bind(opacity, values.Add);
        var player = new AnimationPlayer(clock);
        PlaybackHandle playback = player.Play(CreateClip(opacity), target);
        var completions = 0;
        playback.Completed += _ => completions++;

        clock.Advance(Duration.FromSeconds(2));
        player.Update();
        player.Update();

        var result = new
        {
            Value = values[^1],
            playback.State,
            Completions = completions,
            player.ActiveCount,
        };
        var expected = new
        {
            Value = 1f,
            State = PlaybackState.Completed,
            Completions = 1,
            ActiveCount = 0,
        };
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(PlaybackLoopMode.Repeat, 1.25, 0.25)]
    [InlineData(PlaybackLoopMode.PingPong, 1.25, 0.75)]
    public void LoopModeMapsAbsoluteTimeToClipTime(
        PlaybackLoopMode loopMode,
        double elapsedSeconds,
        float expected)
    {
        var clock = new ManualClock();
        var values = new List<float>();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        var target = new AnimationTarget().Bind(opacity, values.Add);
        var player = new AnimationPlayer(clock);
        player.Play(CreateClip(opacity), target, new PlaybackOptions { LoopMode = loopMode });

        clock.Advance(Duration.FromSeconds(elapsedSeconds));
        player.Update();

        Assert.Equal(expected, values[^1]);
    }

    [Fact]
    public void PlaybackSpeedScalesElapsedClockTime()
    {
        var clock = new ManualClock();
        var values = new List<float>();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        var target = new AnimationTarget().Bind(opacity, values.Add);
        var player = new AnimationPlayer(clock);
        player.Play(CreateClip(opacity), target, new PlaybackOptions { Speed = 2d });

        clock.Advance(Duration.FromSeconds(0.25));
        player.Update();

        Assert.Equal(0.5f, values[^1]);
    }

    [Fact]
    public void ZeroDurationClipCompletesOnFirstUpdate()
    {
        var clock = new ManualClock();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        AnimationTrack<float> track = Track(opacity, Interpolators.Float)[
            Keyframe(Duration.Zero, 1f)
        ];
        var values = new List<float>();
        var player = new AnimationPlayer(clock);
        PlaybackHandle playback = player.Play(
            Clip("Instant")[track],
            new AnimationTarget().Bind(opacity, values.Add));
        var completions = 0;
        playback.Completed += _ => completions++;

        player.Update();

        Assert.Equal([1f], values);
        Assert.Equal(PlaybackState.Completed, playback.State);
        Assert.Equal(1, completions);
    }

    [Fact]
    public void StoppedPlaybackDoesNotApplyFurtherValues()
    {
        var clock = new ManualClock();
        var values = new List<float>();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        var target = new AnimationTarget().Bind(opacity, values.Add);
        var player = new AnimationPlayer(clock);
        PlaybackHandle playback = player.Play(CreateClip(opacity), target);

        playback.Stop();
        clock.Advance(Duration.FromSeconds(0.5));
        player.Update();

        Assert.Equal([0f], values);
        Assert.Equal(PlaybackState.Stopped, playback.State);
    }

    [Fact]
    public void PlayRejectsAnUnboundChannel()
    {
        var clock = new ManualClock();
        AnimationChannel<float> opacity = Channel<float>("Opacity");
        var player = new AnimationPlayer(clock);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            player.Play(CreateClip(opacity), new AnimationTarget()));

        Assert.Contains("Opacity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EquivalentTypedChannelCanBindAClip()
    {
        var clock = new ManualClock();
        var values = new List<float>();
        AnimationChannel<float> clipChannel = Channel<float>("Opacity");
        AnimationChannel<float> targetChannel = Channel<float>("Opacity");
        var player = new AnimationPlayer(clock);

        player.Play(CreateClip(clipChannel), new AnimationTarget().Bind(targetChannel, values.Add));

        Assert.Equal([0f], values);
    }

    private static AnimationClip CreateClip(AnimationChannel<float> channel)
    {
        AnimationTrack<float> track = Track(channel, Interpolators.Float)[
            Keyframe(Duration.Zero, 0f),
            Keyframe(Duration.FromSeconds(1), 1f)
        ];
        return Clip("Fade")[track];
    }
}
