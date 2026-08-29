using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed class AnimationPlayer
{
    private readonly IMonotonicClock clock;
    private readonly List<PlaybackHandle> playbacks = [];

    public AnimationPlayer(IMonotonicClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public int ActiveCount => playbacks.Count(playback => !playback.IsTerminal);

    public PlaybackHandle Play(
        AnimationClip clip,
        AnimationTarget target,
        PlaybackOptions? options = null)
    {
        return Play((IAnimationTimeline)clip, target, options);
    }

    public PlaybackHandle Play(
        IAnimationTimeline timeline,
        AnimationTarget target,
        PlaybackOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(target);
        target.Validate(timeline);

        var playback = new PlaybackHandle(clock, timeline, target, options ?? new PlaybackOptions());
        playback.Start();
        if (!playback.IsTerminal)
        {
            playbacks.Add(playback);
        }

        return playback;
    }

    public void Update()
    {
        TimePoint now = clock.Now;
        var count = playbacks.Count;
        for (var index = 0; index < count; index++)
        {
            playbacks[index].Update(now);
        }

        playbacks.RemoveAll(playback => playback.IsTerminal);
    }

    public void Clear()
    {
        foreach (PlaybackHandle playback in playbacks)
        {
            playback.Stop();
        }

        playbacks.Clear();
    }
}
