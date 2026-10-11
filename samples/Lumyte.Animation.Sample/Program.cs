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

var duration = Duration.FromSeconds(2);
var position = AnimationChannel<float>.Create();
ComposeAnimation.Definitions.Curve<float> motion = Curve<float>(duration, AnimationInterpolators.Float)[
    new AnimationKey<float>(Duration.Zero, 0)
    {
        Timing = AnimationTimings.CubicBezier(0.42, 0, 0.58, 1),
        Interpolator = AnimationInterpolators.CubicBezier(20f, 80f, AnimationInterpolators.Float),
    },
    new AnimationKey<float>(duration, 100)];
ComposeAnimation.Definitions.Curve<Duration> reverseTime = Curve<Duration>(duration, AnimationInterpolators.Duration)[
    new AnimationKey<Duration>(Duration.Zero, duration),
    new AnimationKey<Duration>(duration, Duration.Zero)];
ComposeAnimation.Definitions.Blend<float> blended = Blend<float>(
    from: TimeRemap<float>(value: motion, timeMap: reverseTime),
    to: Sampled<float>(new Tween<float>(0, 0, duration, AnimationInterpolators.Float, AnimationEasing.Linear)),
    weight: Sampled<float>(new Tween<float>(0.5f, 0.5f, duration, AnimationInterpolators.Float, AnimationEasing.Linear)),
    interpolator: AnimationInterpolators.Float);
AnimationTimeline composed = Timeline()[SourceTrack<float>(position, blended)].Build();
var composedClock = new ManualClock();
var composedPlayback = new AnimationPlayback(composedClock, composed);
composedPlayback.Play();
composedClock.Advance(Duration.FromSeconds(1));
output.Clear();
events.Clear();
composedPlayback.Update(output, events);
if (!output.TryGet(position, out float composedValue) || MathF.Abs(composedValue - 25) > 0.0001f)
{
    throw new InvalidOperationException("The composed easing, spatial curve, time map and blend did not produce 25.");
}

Console.WriteLine($"composed midpoint={composedValue:F1}");
