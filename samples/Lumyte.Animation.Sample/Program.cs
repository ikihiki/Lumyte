using Lumyte.Animation;
using Lumyte.Core.Time;
using static Lumyte.Animation.ComposeAnimation;

var opacity = AnimationChannel<float>.Create();
var fadeIn = new Tween<float>(0, 1, Duration.FromSeconds(1), AnimationInterpolators.Float, AnimationEasing.Linear);
ComposeAnimation.Definitions.Timeline forward = Timeline()[Track<float>(opacity, fadeIn)];
AnimationTimeline timeline = Timeline()[Repeat(2)[Sequence()[forward, Reverse()[forward], Marker("Cycle")]]].Build();
var clock = new ManualClock();
var playback = new AnimationPlayback(clock, timeline);
var output = new AnimationOutput();
var events = new List<AnimationEventOccurrence>();
playback.Play();

for (int frame = 0; frame < 8; frame++)
{
    clock.Advance(Duration.FromSeconds(0.5));
    output.Clear();
    events.Clear();
    bool completed = playback.Update(output, events);
    if (!output.TryGet(opacity, out float value))
    {
        throw new InvalidOperationException("The opacity channel did not produce a value.");
    }

    Console.WriteLine($"{playback.Position.TotalSeconds:F1}s opacity={value:F1} events={events.Count} completed={completed}");
}

if (playback.State != AnimationPlaybackState.Completed)
{
    throw new InvalidOperationException("The finite repeated timeline did not complete.");
}
