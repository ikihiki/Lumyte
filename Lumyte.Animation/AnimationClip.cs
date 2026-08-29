using Lumyte.Core.Time;
using Lumyte.Composition;

namespace Lumyte.Animation;

[Composable(Factory = "AnimationKit", Name = "Clip")]
public sealed partial class AnimationClip : IAnimationTimeline
{
    private string name = string.Empty;
    private AnimationTrack[] tracks = [];

    [ComposeParameter]
    public required string Name
    {
        get => name;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            name = value;
        }
    }

    public IReadOnlyList<AnimationTrack> Tracks => tracks;

    public IReadOnlyCollection<AnimationChannel> Channels => tracks.Select(track => track.UntypedChannel).ToArray();

    public Duration Duration { get; private set; }

    [ComposeContent]
    private IReadOnlyList<AnimationTrack> ComposedTracks
    {
        get
        {
            return tracks;
        }

        set
        {
            AnimationTrack[] candidate = [.. value];
            Validate(candidate);
            tracks = candidate;
            Duration = tracks.Length == 0 ? Duration.Zero : tracks.Max(track => track.Duration);
        }
    }

    public AnimationSample Sample(Duration time)
    {
        var values = tracks.ToDictionary(track => track.UntypedChannel, track => track.SampleObject(time));
        return new AnimationSample(this, time, values);
    }

    private static void Validate(AnimationTrack[] tracks)
    {
        if (tracks.Any(track => track is null))
        {
            throw new ArgumentException("Animation clips cannot contain a null track.", nameof(tracks));
        }

        var channels = new HashSet<AnimationChannel>();
        foreach (var track in tracks)
        {
            if (!channels.Add(track.UntypedChannel))
            {
                throw new ArgumentException(
                    $"Animation channels must be unique. The channel '{track.Name}' is duplicated.",
                    nameof(tracks));
            }
        }
    }
}
